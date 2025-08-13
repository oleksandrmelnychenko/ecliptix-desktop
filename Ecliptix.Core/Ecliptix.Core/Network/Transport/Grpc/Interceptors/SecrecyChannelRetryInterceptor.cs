using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Network.Services;
using Ecliptix.Core.Network.Services.Retry;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Transport.Grpc.Interceptors;

public class SecrecyChannelRetryInterceptor : Interceptor
{
    private readonly NetworkProvider _networkProvider;
    private readonly IAsyncPolicy _retryPolicy;
    private readonly ImprovedRetryConfiguration _configuration;
    private readonly INetworkEvents _networkEvents;
    private readonly RequestDeduplicationService _deduplicationService;
    private readonly IPendingRequestManager _pendingRequestManager;

    public SecrecyChannelRetryInterceptor(
        NetworkProvider networkProvider,
        IConfiguration configuration,
        INetworkEvents networkEvents,
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
        string requestId = $"{operationName}_{connectId}_{Guid.NewGuid():N}";
        
        byte[] requestData = System.Text.Encoding.UTF8.GetBytes($"{operationName}_{request}");
        if (await _deduplicationService.IsDuplicateRequestAsync(operationName, requestData, connectId))
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Duplicate request detected"));
        }
        
        // First, try the request without retry policy to detect server shutdown immediately
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
            // Server shutdown detected - register pending request for retry when server recovers
            var taskCompletionSource = new TaskCompletionSource<TResponse>();
            
            _pendingRequestManager.RegisterPendingRequest(requestId, async () =>
            {
                bool connectionResult = await EnsureSecrecyChannelAsync(connectId);
                if (!connectionResult)
                {
                    throw new RpcException(new Status(
                        StatusCode.Unavailable,
                        "Failed to establish SecrecyChannel"));
                }
                
                AsyncUnaryCall<TResponse> retryCall = continuation(request, context);
                return await retryCall.ResponseAsync;
            }, taskCompletionSource);
            
            _networkEvents.InitiateChangeState(
                NetworkStatusChangedEvent.New(NetworkStatus.ServerShutdown));
            
            // Let the original exception propagate to trigger outage mode
            throw;
        }
        catch (RpcException ex)
        {
            // For other RPC errors, use the retry policy
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                if (!ShouldRetry(ex)) throw ex;

                if (!RequiresConnectionRecovery(ex)) throw ex;
                        
                await ReestablishSecrecyChannelAsync(connectId);

                // Try again with fresh connection
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
            
            
            Ecliptix.Utilities.Result<bool, NetworkFailure> restoreResult = await _networkProvider.TryRestoreConnectionAsync(connectId);
            
            if (restoreResult.IsOk && restoreResult.Unwrap())
            {
                return true;
            }
            
            Ecliptix.Utilities.Result<Ecliptix.Protobuf.ProtocolState.EcliptixSecrecyChannelState, NetworkFailure> establishResult = await _networkProvider.EstablishSecrecyChannelAsync(connectId);
            
            if (establishResult.IsOk)
            {
                return true;
            }
            
            
            return false;
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
            
            Ecliptix.Utilities.Result<Ecliptix.Protobuf.ProtocolState.EcliptixSecrecyChannelState, NetworkFailure> result = await _networkProvider.EstablishSecrecyChannelAsync(connectId);
            
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
                onRetry: (exception, delay, retryCount, context) =>
                {
                });

        IAsyncPolicy circuitBreakerPolicy = Policy
            .Handle<RpcException>(ex => IsCircuitBreakerException(ex))
            .CircuitBreakerAsync(
                _configuration.CircuitBreakerThreshold,
                _configuration.CircuitBreakerDuration,
                onBreak: (exception, duration) =>
                {
                },
                onReset: () =>
                {
                },
                onHalfOpen: () =>
                {
                });

        IAsyncPolicy timeoutPolicy = Policy.TimeoutAsync(
            _configuration.HealthCheckTimeout,
            onTimeoutAsync: (context, timespan, task) =>
            {
                return Task.CompletedTask;
            });

        return retryPolicy
            .WrapAsync(circuitBreakerPolicy)
            .WrapAsync(timeoutPolicy);
    }

    private bool ShouldRetry(RpcException ex)
    {
        return ex.StatusCode switch
        {
            StatusCode.Unavailable => true,
            StatusCode.DeadlineExceeded => true,
            StatusCode.ResourceExhausted => true,
            StatusCode.Aborted => true,
            StatusCode.Internal => true,
            StatusCode.Unknown => true,
            StatusCode.DataLoss => false,
            StatusCode.Unauthenticated => false,
            StatusCode.PermissionDenied => false,
            StatusCode.InvalidArgument => false,
            StatusCode.NotFound => false,
            StatusCode.AlreadyExists => false,
            StatusCode.FailedPrecondition => false,
            StatusCode.OutOfRange => false,
            StatusCode.Unimplemented => false,
            StatusCode.Cancelled => false,
            _ => false
        };
    }

    private bool RequiresConnectionRecovery(RpcException ex)
    {
        return ex.StatusCode == StatusCode.Unauthenticated ||
               ex.StatusCode == StatusCode.PermissionDenied ||
               (ex.Status.Detail?.Contains("protocol", StringComparison.OrdinalIgnoreCase) ?? false) ||
               (ex.Status.Detail?.Contains("crypto", StringComparison.OrdinalIgnoreCase) ?? false) ||
               (ex.Status.Detail?.Contains("chain", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private bool IsCircuitBreakerException(RpcException ex)
    {
        return ex.StatusCode == StatusCode.Unavailable ||
               ex.StatusCode == StatusCode.ResourceExhausted;
    }
    
    private bool IsServerShutdown(RpcException ex)
    {
        return ex.StatusCode == StatusCode.Unavailable &&
               ((ex.Status.Detail?.Contains("server", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (ex.Status.Detail?.Contains("shutdown", StringComparison.OrdinalIgnoreCase) ?? false) ||
                (ex.Status.Detail?.Contains("connection refused", StringComparison.OrdinalIgnoreCase) ?? false));
    }

    private uint ExtractConnectionId<TRequest, TResponse>(ClientInterceptorContext<TRequest, TResponse> context)
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
    
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Configuration binding is safe for simple types")]
    [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling", Justification = "Configuration binding is safe for simple types")]
    private static ImprovedRetryConfiguration GetRetryConfiguration(IConfiguration configuration)
    {
        return configuration.GetSection("ImprovedRetryPolicy").Get<ImprovedRetryConfiguration>() 
               ?? ImprovedRetryConfiguration.Production;
    }

    private class SecrecyChannelAwareStreamReader<T> : IAsyncStreamReader<T>
    {
        private readonly Task<bool> _connectionTask;
        private readonly Func<IAsyncStreamReader<T>> _streamFactory;
        private IAsyncStreamReader<T>? _stream;

        public SecrecyChannelAwareStreamReader(
            Task<bool> connectionTask,
            Func<IAsyncStreamReader<T>> streamFactory)
        {
            _connectionTask = connectionTask;
            _streamFactory = streamFactory;
        }

        public T Current => _stream != null ? _stream.Current : default!;

        public async Task<bool> MoveNext(System.Threading.CancellationToken cancellationToken)
        {
            if (_stream == null)
            {
                bool connected = await _connectionTask;
                if (!connected)
                {
                    throw new RpcException(new Status(
                        StatusCode.Unavailable,
                        "Failed to establish SecrecyChannel for streaming"));
                }
                
                _stream = _streamFactory();
            }
            
            return await _stream.MoveNext(cancellationToken);
        }
    }
}