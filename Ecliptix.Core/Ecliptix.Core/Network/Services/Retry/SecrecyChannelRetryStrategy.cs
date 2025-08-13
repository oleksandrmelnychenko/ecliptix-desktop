using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Polly;
using Polly.Contrib.WaitAndRetry;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Network.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Services.Retry;

public class SecrecyChannelRetryStrategy : IRetryStrategy, IDisposable
{
    private readonly ImprovedRetryConfiguration _configuration;
    private readonly IAsyncPolicy<object> _retryPolicy;
    private Lazy<NetworkProvider>? _lazyNetworkProvider;

    public SecrecyChannelRetryStrategy(
        IConfiguration configuration)
    {
        _configuration = GetRetryConfiguration(configuration);
        _retryPolicy = CreateRetryPolicy();
    }

    public void SetLazyNetworkProvider(Lazy<NetworkProvider> lazyNetworkProvider)
    {
        _lazyNetworkProvider = lazyNetworkProvider;
    }
    
    private NetworkProvider? GetNetworkProvider()
    {
        if (_lazyNetworkProvider != null)
            return _lazyNetworkProvider.Value;
            
        return null;
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId = null,
        int maxRetries = 15,
        CancellationToken cancellationToken = default)
    {
        if (!connectId.HasValue)
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType("Connection ID is required"));
        }

        Context context = new Context
        {
            ["OperationName"] = operationName,
            ["ConnectId"] = connectId.Value,
            ["MaxRetries"] = maxRetries
        };

        try
        {
            object result = await _retryPolicy.ExecuteAsync(
                async (ctx, ct) => await operation(),
                context,
                cancellationToken);

            return (Result<TResponse, NetworkFailure>)result;
        }
        catch (OperationCanceledException)
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding("Operation cancelled"));
        }
        catch (Exception ex)
        {
            return Result<TResponse, NetworkFailure>.Err(
                NetworkFailure.DataCenterNotResponding($"Unexpected error: {ex.Message}"));
        }
    }

    public async Task<Result<TResponse, NetworkFailure>> ExecuteSecrecyChannelOperationAsync<TResponse>(
        Func<Task<Result<TResponse, NetworkFailure>>> operation,
        string operationName,
        uint? connectId,
        System.Collections.Generic.IReadOnlyList<TimeSpan> backoffSchedule,
        bool useJitter = true,
        double jitterRatio = 0.25,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteSecrecyChannelOperationAsync(
            operation,
            operationName,
            connectId,
            backoffSchedule.Count,
            cancellationToken);
    }

    private async Task<bool> EnsureSecrecyChannelAsync(uint connectId, CancellationToken cancellationToken)
    {
        NetworkProvider? networkProvider = GetNetworkProvider();
        if (networkProvider == null)
        {
            return false;
        }

        try
        {
            if (networkProvider.IsConnectionHealthy(connectId))
            {
                return true;
            }


            Result<bool, NetworkFailure> restoreResult = await networkProvider.TryRestoreConnectionAsync(connectId);
            
            if (restoreResult.IsOk && restoreResult.Unwrap())
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

    private IAsyncPolicy<object> CreateRetryPolicy()
    {
        System.Collections.Generic.IEnumerable<TimeSpan> rawDelays = _configuration.UseAdaptiveRetry 
            ? Backoff.DecorrelatedJitterBackoffV2(
                medianFirstRetryDelay: _configuration.InitialRetryDelay,
                retryCount: _configuration.MaxRetries,
                fastFirst: true)
            : Backoff.ExponentialBackoff(
                initialDelay: _configuration.InitialRetryDelay,
                retryCount: _configuration.MaxRetries,
                factor: 2.0,
                fastFirst: true);
        
        TimeSpan[] retryDelays = rawDelays.Select(delay => 
            delay > _configuration.MaxRetryDelay ? _configuration.MaxRetryDelay : delay).ToArray();

        IAsyncPolicy<object> retryPolicy = Policy
            .HandleResult<object>(result =>
            {
                if (result is Result<object, NetworkFailure> res)
                    return res.IsErr && ShouldRetry(res.UnwrapErr());
                
                if (TryGetNetworkFailureFromResult(result, out NetworkFailure? failure))
                {
                    return ShouldRetry(failure);
                }
                return false;
            })
            .WaitAndRetryAsync(
                retryDelays,
                onRetry: async (outcome, delay, retryCount, context) =>
                {
                    object operation = context.ContainsKey("OperationName") ? context["OperationName"] : "Unknown";
                    object connectId = context.ContainsKey("ConnectId") ? context["ConnectId"] : 0;
                    

                    if (RequiresConnectionRecovery(outcome.Result))
                    {
                        await EnsureSecrecyChannelAsync((uint)connectId, CancellationToken.None);
                    }
                });

        IAsyncPolicy<object> circuitBreakerPolicy = Policy
            .HandleResult<object>(result =>
            {
                if (result is Result<object, NetworkFailure> res)
                    return res.IsErr && IsCircuitBreakerFailure(res.UnwrapErr());
                return false;
            })
            .CircuitBreakerAsync(
                _configuration.CircuitBreakerThreshold,
                _configuration.CircuitBreakerDuration,
                onBreak: (outcome, duration) =>
                {
                },
                onReset: () =>
                {
                });

        IAsyncPolicy timeoutPolicy = Policy.TimeoutAsync(
            TimeSpan.FromMinutes(1),
            onTimeoutAsync: (context, timespan, task) =>
            {
                return Task.CompletedTask;
            });

        return retryPolicy
            .WrapAsync(circuitBreakerPolicy)
            .WrapAsync(timeoutPolicy);
    }

    private bool ShouldRetry(NetworkFailure failure)
    {
        return FailureClassification.IsTransient(failure);
    }

    private bool RequiresConnectionRecovery(object? result)
    {
        if (result is Result<object, NetworkFailure> res && res.IsErr)
        {
            NetworkFailure failure = res.UnwrapErr();
            return FailureClassification.IsProtocolStateMismatch(failure) ||
                   FailureClassification.IsChainRotationMismatch(failure) ||
                   FailureClassification.IsCryptoDesync(failure) ||
                   failure.Message.Contains("Connection unavailable", StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    private bool IsCircuitBreakerFailure(NetworkFailure failure)
    {
        return FailureClassification.IsServerShutdown(failure);
    }

    public void ResetConnectionState(uint? connectId = null)
    {
        if (connectId.HasValue)
        {
            GetNetworkProvider()?.ClearConnection(connectId.Value);
        }
    }

    public RetryMetrics GetRetryMetrics(uint? connectId = null)
    {
        return new RetryMetrics(0, 0, 0, TimeSpan.Zero, DateTime.MinValue, DateTime.MinValue);
    }

    public ConnectionRetryState? GetConnectionState(uint connectId)
    {
        bool isHealthy = GetNetworkProvider()?.IsConnectionHealthy(connectId) ?? false;
        return new ConnectionRetryState(
            connectId,
            0,
            DateTime.MinValue,
            null,
            !isHealthy,
            !isHealthy ? DateTime.UtcNow : null);
    }

    public void MarkConnectionHealthy(uint connectId)
    {
    }

    public bool IsConnectionHealthy(uint connectId)
    {
        return GetNetworkProvider()?.IsConnectionHealthy(connectId) ?? false;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Configuration binding is safe for simple types")]
    [UnconditionalSuppressMessage("AOT", "IL3050:Calling members annotated with 'RequiresDynamicCodeAttribute' may break functionality when AOT compiling", Justification = "Configuration binding is safe for simple types")]
    private static ImprovedRetryConfiguration GetRetryConfiguration(IConfiguration configuration)
    {
        return configuration.GetSection("ImprovedRetryPolicy").Get<ImprovedRetryConfiguration>() 
               ?? ImprovedRetryConfiguration.Production;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2075:'this' argument does not satisfy 'DynamicallyAccessedMemberTypes' requirements", Justification = "Reflection is used safely on known Result types")]
    private static bool TryGetNetworkFailureFromResult(object result, [NotNullWhen(true)] out NetworkFailure? failure)
    {
        failure = null;
        
        try
        {
            var type = result.GetType();
            var isErrProperty = type.GetProperty("IsErr");
            if (isErrProperty != null && (bool)isErrProperty.GetValue(result)!)
            {
                var unwrapErrMethod = type.GetMethod("UnwrapErr");
                if (unwrapErrMethod != null)
                {
                    failure = unwrapErrMethod.Invoke(result, null) as NetworkFailure;
                    return failure != null;
                }
            }
        }
        catch
        {
            // Reflection failed, return false
        }
        
        return false;
    }

    public void Dispose()
    {
    }
}