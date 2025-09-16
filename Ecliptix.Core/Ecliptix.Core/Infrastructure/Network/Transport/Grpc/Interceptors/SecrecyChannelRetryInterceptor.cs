using System;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Ecliptix.Core.Infrastructure.Network.Transport.Grpc.Interceptors;

public class SecrecyChannelRetryInterceptor : Interceptor
{
    private readonly NetworkProvider _networkProvider;
    private readonly INetworkEventService _networkEvents;

    public SecrecyChannelRetryInterceptor(
        NetworkProvider networkProvider,
        INetworkEventService networkEvents)
    {
        _networkProvider = networkProvider;
        _networkEvents = networkEvents;
    }

    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        AsyncUnaryCall<TResponse> newCall = continuation(request, context);

        return new AsyncUnaryCall<TResponse>(
            HandleUnaryAsync(newCall.ResponseAsync, request, context, continuation),
            newCall.ResponseHeadersAsync,
            newCall.GetStatus,
            newCall.GetTrailers,
            newCall.Dispose);
    }

    public override AsyncServerStreamingCall<TResponse> AsyncServerStreamingCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncServerStreamingCallContinuation<TRequest, TResponse> continuation)
    {
        uint connectId = ExtractConnectionId(context);
        Task<bool> connectionTask = EnsureSecrecyChannelAsync(connectId);

        return new AsyncServerStreamingCall<TResponse>(
            new SecrecyChannelAwareStreamReader<TResponse>(
                connectionTask,
                () => continuation(request, context).ResponseStream),
            continuation(request, context).ResponseHeadersAsync,
            continuation(request, context).GetStatus,
            continuation(request, context).GetTrailers,
            continuation(request, context).Dispose);
    }

    private async Task<TResponse> HandleUnaryAsync<TRequest, TResponse>(
        Task<TResponse> responseTask,
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        uint connectId = ExtractConnectionId(context);

        try
        {
            bool connectionResult = await EnsureSecrecyChannelAsync(connectId);
            if (!connectionResult)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "Failed to establish SecrecyChannel"));
            }

            TResponse result = await responseTask;
            return result;
        }
        catch (RpcException ex) when (IsServerShutdown(ex))
        {
            await _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ServerShutdown);
            throw;
        }
        catch (RpcException ex) when (ShouldRetry(ex) && RequiresConnectionRecovery(ex))
        {
            await ReestablishSecrecyChannelAsync(connectId);
            bool connectionResult = await EnsureSecrecyChannelAsync(connectId);
            if (!connectionResult)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "Failed to establish SecrecyChannel"));
            }

            AsyncUnaryCall<TResponse> retryCall = continuation(request, context);
            return await retryCall.ResponseAsync;
        }
    }

    private async Task<bool> EnsureSecrecyChannelAsync(uint connectId)
    {
        try
        {
            if (_networkProvider.IsConnectionHealthy(connectId))
            {
                return true;
            }

            Utilities.Result<bool, NetworkFailure> restoreResult =
                await _networkProvider.TryRestoreConnectionAsync(connectId);

            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                return true;
            }

            Utilities.Result<Protobuf.ProtocolState.EcliptixSessionState, NetworkFailure> establishResult =
                await _networkProvider.EstablishSecrecyChannelAsync(connectId);

            return establishResult.IsOk;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task ReestablishSecrecyChannelAsync(uint connectId)
    {
        try
        {
            _networkProvider.ClearConnection(connectId);

            Utilities.Result<Protobuf.ProtocolState.EcliptixSessionState, NetworkFailure> result =
                await _networkProvider.EstablishSecrecyChannelAsync(connectId);

            if (result.IsErr)
            {
            }
        }
        catch (Exception)
        {

        }
    }


    private static bool ShouldRetry(RpcException ex)
    {
        return ex.StatusCode switch
        {
            StatusCode.Unavailable => true,
            StatusCode.DeadlineExceeded => true,
            StatusCode.ResourceExhausted => true,
            StatusCode.Aborted => true,
            StatusCode.Internal => true,
            StatusCode.Unknown => true,
            _ => false
        };
    }

    private static bool RequiresConnectionRecovery(RpcException ex)
    {
        return ex.StatusCode == StatusCode.Unauthenticated ||
               ex.StatusCode == StatusCode.PermissionDenied ||
               (ex.Status.Detail?.Contains("protocol", StringComparison.OrdinalIgnoreCase) ?? false) ||
               (ex.Status.Detail?.Contains("crypto", StringComparison.OrdinalIgnoreCase) ?? false) ||
               (ex.Status.Detail?.Contains("chain", StringComparison.OrdinalIgnoreCase) ?? false);
    }


    private static bool IsServerShutdown(RpcException ex)
    {
        return ex.StatusCode == StatusCode.Unavailable &&
               ((ex.Status.Detail?.Contains("server", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (ex.Status.Detail?.Contains("shutdown", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (ex.Status.Detail?.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private static uint ExtractConnectionId<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
        where TRequest : class
        where TResponse : class
    {
        Metadata.Entry? connectIdEntry = context.Options.Headers?.FirstOrDefault(h => h.Key == "connect-id");
        if (connectIdEntry != null && uint.TryParse(connectIdEntry.Value, out uint connectId))
        {
            return connectId;
        }

        return 1;
    }


    private class SecrecyChannelAwareStreamReader<T>(
        Task<bool> connectionTask,
        Func<IAsyncStreamReader<T>> streamFactory)
        : IAsyncStreamReader<T>
    {
        private IAsyncStreamReader<T>? _stream;

        public T Current => _stream != null ? _stream.Current : default!;

        public async Task<bool> MoveNext(System.Threading.CancellationToken cancellationToken)
        {
            if (_stream != null) return await _stream.MoveNext(cancellationToken);
            bool connected = await connectionTask;
            if (!connected)
            {
                throw new RpcException(new Status(
                    StatusCode.Unavailable,
                    "Failed to establish SecrecyChannel for streaming"));
            }

            _stream = streamFactory();

            return await _stream.MoveNext(cancellationToken);
        }
    }
}