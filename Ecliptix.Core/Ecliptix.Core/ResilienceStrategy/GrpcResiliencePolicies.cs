using System;
using System.Net.Http;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;
using Grpc.Core;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Serilog;

namespace Ecliptix.Core.ResilienceStrategy;

public static class GrpcResiliencePolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetAuthenticatedPolicy(ISessionManager sessionManager)
    {
        var transientRetryPolicy = Policy<HttpResponseMessage>
            .Handle<HttpRequestException>()
            .Or<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable ||
                                    ex.StatusCode == StatusCode.DeadlineExceeded ||
                                    ex.StatusCode == StatusCode.ResourceExhausted)
            .OrResult(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                3,
                retryAttempt => TimeSpan.FromSeconds(retryAttempt),
                onRetry: (outcome, timespan, retryAttempt, context) =>
                {
                    Log.Warning("Transient failure. Retrying in {Timespan} seconds. Attempt {RetryAttempt}/3",
                        timespan.TotalSeconds, retryAttempt);
                });

        var circuitBreakerPolicy = Policy<HttpResponseMessage>
            .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unauthenticated)
            .OrResult(r => r.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            .CircuitBreakerAsync(
                2,
                TimeSpan.FromSeconds(30),
                onBreak: (result, breakDuration) =>
                {
                    Log.Warning("Circuit breaker opened for {BreakDuration} seconds due to unauthorized failure",
                        breakDuration.TotalSeconds);
                    sessionManager.MarkSessionAsUnhealthy();
                },
                onReset: () => Log.Information("Circuit breaker reset")
            );

        var sessionRecoveryPolicy = Policy<HttpResponseMessage>
            .Handle<BrokenCircuitException>()
            .WaitAndRetryAsync(
                2,
                _ => TimeSpan.FromSeconds(1),
                onRetryAsync: async (outcome, timespan, retryAttempt, context) =>
                {
                    Log.Warning("Circuit broken. Attempting session recovery ({RetryAttempt}/2)", retryAttempt);
                    var recoveryResult = await sessionManager.ReEstablishSessionAsync();
                    if (recoveryResult.IsErr)
                    {
                        Log.Error("Session recovery failed: {Error}", recoveryResult.UnwrapErr().Message);
                        throw new SessionRecoveryException("Failed to recover session",
                            recoveryResult.UnwrapErr().InnerException);
                    }

                    Log.Information("Session recovered on attempt {RetryAttempt}", retryAttempt);
                });

        // Adjusted order: circuit breaker first
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
    
    public static AsyncRetryPolicy<RestoreSecrecyChannelResponse> GetRestoreSecrecyChannelRetryPolicy()
    {
        return Policy<RestoreSecrecyChannelResponse>
            .Handle<RpcException>(ex => ex.StatusCode == StatusCode.Unavailable ||
                                        ex.StatusCode == StatusCode.DeadlineExceeded ||
                                        ex.StatusCode == StatusCode.ResourceExhausted)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(retryAttempt * 2),
                onRetry: (exception, timespan, retryAttempt, context) =>
                {
                    Log.Warning(
                        "gRPC call failed . Retrying in {Timespan} seconds. Attempt {RetryAttempt}/3",
                        timespan.TotalSeconds, retryAttempt);
                });
    }
}

public class SessionRecoveryException : Exception
{
    public SessionRecoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}