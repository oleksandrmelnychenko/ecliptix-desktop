using System;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Network.Resilience;

public delegate bool ShouldRetryDelegate<TResponse>(Result<TResponse, NetworkFailure> result);
public delegate bool RequiresConnectionRecoveryDelegate<TResponse>(Result<TResponse, NetworkFailure> result);

public static class RetryDecisionFactory
{
    public static ShouldRetryDelegate<TResponse> CreateShouldRetryDelegate<TResponse>()
    {
        return static result => result.IsErr && FailureClassification.IsTransient(result.UnwrapErr());
    }

    public static RequiresConnectionRecoveryDelegate<TResponse> CreateConnectionRecoveryDelegate<TResponse>()
    {
        return static result =>
        {
            if (result.IsOk) return false;

            NetworkFailure failure = result.UnwrapErr();
            return FailureClassification.IsProtocolStateMismatch(failure) ||
                   FailureClassification.IsCryptoDesync(failure);
        };
    }
}
