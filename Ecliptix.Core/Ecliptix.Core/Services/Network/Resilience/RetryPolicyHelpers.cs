using System;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Network.Resilience;

/// <summary>
/// AOT-compatible delegate for determining if a result should be retried.
/// This eliminates reflection-based type checking for AOT compatibility.
/// </summary>
/// <param name="result">The result to evaluate</param>
/// <returns>True if the operation should be retried, false otherwise</returns>
public delegate bool ShouldRetryDelegate<TResponse>(Result<TResponse, NetworkFailure> result);

/// <summary>
/// AOT-compatible delegate for determining if a result requires connection recovery.
/// </summary>
/// <param name="result">The result to evaluate</param>
/// <returns>True if connection recovery is needed, false otherwise</returns>
public delegate bool RequiresConnectionRecoveryDelegate<TResponse>(Result<TResponse, NetworkFailure> result);

/// <summary>
/// AOT-compatible delegate for determining if a result should trigger circuit breaker.
/// </summary>
/// <param name="result">The result to evaluate</param>
/// <returns>True if circuit breaker should be triggered, false otherwise</returns>
public delegate bool IsCircuitBreakerFailureDelegate<TResponse>(Result<TResponse, NetworkFailure> result);

/// <summary>
/// Static factory for creating AOT-compatible retry decision delegates.
/// This replaces the reflection-based approach with compile-time type-safe delegates.
/// </summary>
public static class RetryDecisionFactory
{
    /// <summary>
    /// Creates a should-retry delegate for the specified type.
    /// </summary>
    public static ShouldRetryDelegate<TResponse> CreateShouldRetryDelegate<TResponse>()
    {
        return static result => result.IsErr && FailureClassification.IsTransient(result.UnwrapErr());
    }

    /// <summary>
    /// Creates a connection recovery delegate for the specified type.
    /// </summary>
    public static RequiresConnectionRecoveryDelegate<TResponse> CreateConnectionRecoveryDelegate<TResponse>()
    {
        return static result =>
        {
            if (result.IsOk) return false;

            NetworkFailure failure = result.UnwrapErr();
            return FailureClassification.IsProtocolStateMismatch(failure) ||
                   FailureClassification.IsChainRotationMismatch(failure) ||
                   FailureClassification.IsCryptoDesync(failure) ||
                   failure.Message.Contains("Connection unavailable", StringComparison.OrdinalIgnoreCase);
        };
    }

    /// <summary>
    /// Creates a circuit breaker failure delegate for the specified type.
    /// </summary>
    public static IsCircuitBreakerFailureDelegate<TResponse> CreateCircuitBreakerDelegate<TResponse>()
    {
        return static result => result.IsErr && FailureClassification.IsServerShutdown(result.UnwrapErr());
    }
}