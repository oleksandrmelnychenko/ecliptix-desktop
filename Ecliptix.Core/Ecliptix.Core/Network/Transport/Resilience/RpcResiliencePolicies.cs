using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Sockets;
using Ecliptix.Core.AppEvents.Network;
using Grpc.Core;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Serilog;

namespace Ecliptix.Core.Network.Transport.Resilience;

public static class RpcResiliencePolicies
{
    private const int DefaultRetryCount = 3;

    public static IAsyncPolicy<HttpResponseMessage> CreateUnaryResiliencePolicy(
        INetworkEvents networkEvents
    )
    {
        const double baseBackoffSeconds = 2.0;
        const int breakerFailures = 2;
        const int breakerDurationSeconds = 30;
        const int recoveryDelaySeconds = 2;

        AsyncRetryPolicy<HttpResponseMessage>? transientRetryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<RpcException>(ex =>
                ex.StatusCode
                    is StatusCode.Unavailable
                        or StatusCode.DeadlineExceeded
                        or StatusCode.ResourceExhausted
            )
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                DefaultRetryCount,
                retryAttempt =>
                    TimeSpan.FromSeconds(
                        baseBackoffSeconds * retryAttempt + Random.Shared.NextDouble()
                    ),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    Exception exception = outcome.Exception;
                    Log.Warning(
                        exception,
                        "Transient failure. Retrying in {Timespan} seconds. Attempt {RetryAttempt}/{RetryCount}",
                        timespan.TotalSeconds,
                        retryAttempt
                    );
                }
            );

        AsyncCircuitBreakerPolicy<HttpResponseMessage>? circuitBreakerPolicy =
            Policy<HttpResponseMessage>
                .Handle<RpcException>(ex =>
                    ex.StatusCode
                        is StatusCode.Unavailable
                            or StatusCode.DeadlineExceeded
                            or StatusCode.ResourceExhausted
                )
                .OrResult(r => (int)r.StatusCode >= 500)
                .CircuitBreakerAsync(
                    breakerFailures,
                    TimeSpan.FromSeconds(breakerDurationSeconds),
                    onBreak: (result, breakDuration) =>
                    {
                        Log.Warning(
                            "Circuit breaker opened for {BreakDuration} seconds due to transient failure",
                            breakDuration.TotalSeconds
                        );
                        networkEvents.InitiateChangeState(
                            NetworkStatusChangedEvent.New(NetworkStatus.DataCenterDisconnected)
                        );
                    },
                    onReset: () => Log.Information("Circuit breaker reset")
                );

        AsyncRetryPolicy<HttpResponseMessage>? sessionRecoveryPolicy = Policy<HttpResponseMessage>
            .Handle<BrokenCircuitException>()
            .WaitAndRetryAsync(
                DefaultRetryCount - 1,
                _ => TimeSpan.FromSeconds(recoveryDelaySeconds),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    Log.Warning(
                        "Circuit broken. Attempting session recovery ({RetryAttempt}/{MaxRetries})",
                        retryAttempt,
                        DefaultRetryCount - 1
                    );

                    networkEvents.InitiateChangeState(
                        NetworkStatusChangedEvent.New(NetworkStatus.RestoreSecrecyChannel)
                    );
                }
            );

        return Policy.WrapAsync(sessionRecoveryPolicy, circuitBreakerPolicy, transientRetryPolicy);
    }

    public static IAsyncPolicy<TResponse> CreateGrpcResiliencePolicy<TResponse>()
    {
        const int retryCount = 3;
        const double baseBackoffSeconds = 2.0;

        AsyncRetryPolicy<TResponse>? retryPolicy = Policy<TResponse>
            .Handle<RpcException>(ex =>
                ex.StatusCode
                    is StatusCode.Unavailable
                        or StatusCode.DeadlineExceeded
                        or StatusCode.ResourceExhausted
            )
            .Or<SocketException>()
            .WaitAndRetryAsync(
                retryCount,
                retryAttempt => TimeSpan.FromSeconds(baseBackoffSeconds * retryAttempt),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    Log.Warning(
                        outcome.Exception,
                        "gRPC call failed. Retrying in {Timespan} seconds. Attempt {RetryAttempt}/{RetryCount}",
                        timespan.TotalSeconds,
                        retryAttempt,
                        retryCount
                    );
                }
            );

        return retryPolicy;
    }
}
