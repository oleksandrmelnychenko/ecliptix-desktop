using System;
using System.Net.Http;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Grpc.Core;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Serilog;

namespace Ecliptix.Core.ResilienceStrategy;

public static class GrpcResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetAuthenticatedPolicy(INetworkProvider networkProvider)
    {
        AsyncRetryPolicy<HttpResponseMessage>? transientRetryPolicy = Policy<HttpResponseMessage>
            .Handle<RpcException>(ex =>
                ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted)
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(3,
                retryAttempt => TimeSpan.FromSeconds(retryAttempt),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    Log.Warning("Transient failure. Retrying in {Timespan} seconds. Attempt {RetryAttempt}/3",
                        timespan.TotalSeconds, retryAttempt);
                });

        AsyncCircuitBreakerPolicy<HttpResponseMessage>? circuitBreakerPolicy = Policy<HttpResponseMessage>
            .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unauthenticated)
            .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            .CircuitBreakerAsync(
                2,
                TimeSpan.FromSeconds(30),
                onBreak: (result, breakDuration) =>
                {
                    Log.Warning("Circuit breaker opened for {BreakDuration} seconds due to unauthorized failure",
                        breakDuration.TotalSeconds);
                    networkProvider.SetSecrecyChannelAsUnhealthy();
                },
                onReset: () => Log.Information("Circuit breaker reset")
            );

        AsyncRetryPolicy<HttpResponseMessage>? sessionRecoveryPolicy = Policy<HttpResponseMessage>
            .Handle<BrokenCircuitException>()
            .WaitAndRetryAsync(
                2,
                _ => TimeSpan.FromSeconds(1),
                onRetryAsync: async (outcome, timespan, retryAttempt, context) =>
                {
                    Log.Warning("Circuit broken. Attempting session recovery ({RetryAttempt}/2)", retryAttempt);
                    Result<Unit, EcliptixProtocolFailure> recoveryResult =
                        await networkProvider.RestoreSecrecyChannelAsync();
                    if (recoveryResult.IsErr)
                    {
                        Log.Error("Session recovery failed: {Error}", recoveryResult.UnwrapErr().Message);
                        throw new SessionRecoveryException("Failed to recover session",
                            recoveryResult.UnwrapErr().InnerException);
                    }

                    Log.Information("Session recovered on attempt {RetryAttempt}", retryAttempt);
                });

        return Policy.WrapAsync(circuitBreakerPolicy, sessionRecoveryPolicy, transientRetryPolicy);
    }

    public static AsyncRetryPolicy<HttpResponseMessage> GetUnauthenticatedRetryPolicy()
    {
        return Policy
            .Handle<HttpRequestException>()
            .Or<RpcException>(ex => ex.StatusCode is StatusCode.Unavailable
                or StatusCode.DeadlineExceeded
                or StatusCode.ResourceExhausted)
            .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt =>
                    TimeSpan.FromSeconds(retryAttempt * 2),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    Exception exception = outcome.Exception ??
                                          new Exception($"HTTP response: {outcome.Result?.StatusCode}");
                    Log.Warning(
                        exception,
                        "Unauthenticated call failed. Retrying in {Timespan} seconds. Attempt {RetryAttempt}/3",
                        timespan.TotalSeconds, retryAttempt);
                });
    }

    public static AsyncRetryPolicy<TResult> GetSecrecyChannelRetryPolicy<TResult>() =>
        Policy<TResult>
            .Handle<RpcException>(ex =>
                ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(retryAttempt * 10),
                onRetry: (exception, timespan, retryAttempt, context) =>
                {
                    Log.Warning("gRPC call failed . Retrying in {Timespan} seconds. Attempt {RetryAttempt}/3",
                        timespan.TotalSeconds, retryAttempt);
                });
}

public class SessionRecoveryException : Exception
{
    public SessionRecoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}