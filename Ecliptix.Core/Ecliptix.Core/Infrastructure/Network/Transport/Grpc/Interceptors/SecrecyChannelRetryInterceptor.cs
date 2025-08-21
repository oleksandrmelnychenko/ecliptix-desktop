using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Network.Infrastructure;
using Ecliptix.Core.Services.Network.Resilience;
using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Network.Transport.Grpc.Interceptors;

public class SecrecyChannelRetryInterceptor : Interceptor
{
    private readonly NetworkProvider _networkProvider;
    private readonly IAsyncPolicy _retryPolicy;
    private readonly ImprovedRetryConfiguration _configuration;
    private readonly INetworkEventService _networkEvents;
    private readonly RequestDeduplicationService _deduplicationService;
    private readonly IPendingRequestManager _pendingRequestManager;

    public SecrecyChannelRetryInterceptor(
        NetworkProvider networkProvider,
        IConfiguration configuration,
        INetworkEventService networkEvents,
        RequestDeduplicationService deduplicationService,
        IPendingRequestManager pendingRequestManager)
    {
        _networkProvider = networkProvider;
        _networkEvents = networkEvents;
        _deduplicationService = deduplicationService;
        _pendingRequestManager = pendingRequestManager;
        _configuration = GetRetryConfiguration(configuration);

        _retryPolicy = CreateRetryPolicy();
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
        string operationName = context.Method.FullName;

        string? existingRequestId = context.Options.Headers?.Get("request-id")?.Value;
        string requestId = existingRequestId ?? $"{operationName}_{connectId}_{Guid.NewGuid():N}";

        byte[] requestIdBytes = System.Text.Encoding.UTF8.GetBytes(requestId);
        if (await _deduplicationService.IsDuplicateRequestAsync(operationName, requestIdBytes, connectId))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists,
                $"Duplicate request detected for {operationName}, rejecting"));
        }

        try
        {
            bool connectionResult = await EnsureSecrecyChannelAsync(connectId);

            if (!connectionResult)
            {
                throw new RpcException(new Status(
                    StatusCode.Unavailable,
                    "Failed to establish SecrecyChannel"));
            }

            TResponse result = await responseTask;
            return result;
        }
        catch (RpcException ex) when (IsServerShutdown(ex))
        {
            TaskCompletionSource<TResponse> taskCompletionSource = new();

            _pendingRequestManager.RegisterPendingRequest(requestId, async () =>
            {
                bool connectionRestored = await EnsureSecrecyChannelAsync(connectId);
                if (!connectionRestored)
                {
                    Log.Information("🔄 PENDING REQUEST: Connection not restored for {RequestId}, will retry later", requestId);
                    throw new RpcException(new Status(StatusCode.Unavailable, "Failed to restore connection"));
                }

                _deduplicationService.RemoveRequest(operationName, requestIdBytes, connectId);

                AsyncUnaryCall<TResponse> retryCall = continuation(request, context);
                TResponse result = await retryCall.ResponseAsync;

                Log.Information("🔄 PENDING REQUEST SUCCESS: Request {RequestId} succeeded on retry", requestId);
            });

            await _networkEvents.NotifyNetworkStatusAsync(NetworkStatus.ServerShutdown);

            taskCompletionSource.SetException(new RpcException(new Status(StatusCode.Unavailable,
                $"Server shutdown detected. Request {requestId} has been queued for retry.")));

            return await taskCompletionSource.Task;
        }
        catch (RpcException ex)
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                if (!ShouldRetry(ex) || !RequiresConnectionRecovery(ex)) throw ex;

                await ReestablishSecrecyChannelAsync(connectId);

                bool connectionResult = await EnsureSecrecyChannelAsync(connectId);
                if (!connectionResult)
                {
                    throw new RpcException(new Status(
                        StatusCode.Unavailable,
                        "Failed to establish SecrecyChannel"));
                }

                AsyncUnaryCall<TResponse> retryCall = continuation(request, context);
                return await retryCall.ResponseAsync;
            });
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

            Utilities.Result<Protobuf.ProtocolState.EcliptixSecrecyChannelState, NetworkFailure> establishResult =
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

            Utilities.Result<Protobuf.ProtocolState.EcliptixSecrecyChannelState, NetworkFailure> result =
                await _networkProvider.EstablishSecrecyChannelAsync(connectId);

            if (result.IsErr)
            {
            }
        }
        catch (Exception)
        {

        }
    }

    private IAsyncPolicy CreateRetryPolicy()
    {
        System.Collections.Generic.IEnumerable<TimeSpan> retryDelays = _configuration.UseAdaptiveRetry
            ? Backoff.DecorrelatedJitterBackoffV2(
                medianFirstRetryDelay: _configuration.InitialRetryDelay,
                retryCount: _configuration.MaxRetries,
                fastFirst: true)
            : Backoff.ExponentialBackoff(
                initialDelay: _configuration.InitialRetryDelay,
                retryCount: _configuration.MaxRetries,
                factor: 2.0,
                fastFirst: true);

        IAsyncPolicy retryPolicy = Policy
            .Handle<RpcException>(ShouldRetry)
            .Or<TaskCanceledException>()
            .Or<OperationCanceledException>()
            .WaitAndRetryAsync(
                retryDelays,
                onRetry: (_, _, _, _) => { });

        IAsyncPolicy timeoutPolicy = Policy.TimeoutAsync(
            TimeSpan.FromSeconds(30),
            onTimeoutAsync: (_, _, _) => Task.CompletedTask);

        return retryPolicy
            .WrapAsync(timeoutPolicy);
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

    private static bool IsCircuitBreakerException(RpcException ex)
    {
        return ex.StatusCode == StatusCode.Unavailable ||
               ex.StatusCode == StatusCode.ResourceExhausted;
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

    [UnconditionalSuppressMessage("Trimming",
        "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code",
        Justification = "Configuration binding is safe for simple types")]
    [UnconditionalSuppressMessage("AOT",
        "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling",
        Justification = "Configuration binding is safe for simple types")]
    private static ImprovedRetryConfiguration GetRetryConfiguration(IConfiguration configuration)
    {
        return configuration.GetSection("ImprovedRetryPolicy").Get<ImprovedRetryConfiguration>()
               ?? ImprovedRetryConfiguration.Production;
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