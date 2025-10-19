using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Network.Resilience;

public static class FailureClassification
{
    public static bool IsTransient(NetworkFailure failure)
    {
        if (failure.FailureType == NetworkFailureType.OperationCancelled)
            return false;

        if (failure.UserError?.Retryable is bool retryable)
        {
            return retryable;
        }

        return failure.FailureType == NetworkFailureType.DataCenterNotResponding;
    }

    public static bool IsServerShutdown(NetworkFailure failure)
    {
        return failure.FailureType == NetworkFailureType.DataCenterShutdown;
    }

    public static bool IsCryptoDesync(NetworkFailure failure)
    {
        return failure.FailureType == NetworkFailureType.RsaEncryptionFailure;
    }

    public static bool IsProtocolStateMismatch(NetworkFailure failure)
    {
        return failure.FailureType == NetworkFailureType.ProtocolStateMismatch;
    }

    public static bool IsChainRotationMismatch(NetworkFailure failure)
    {
        return failure.FailureType == NetworkFailureType.ProtocolStateMismatch;
    }

    public static bool IsCancellation(NetworkFailure failure)
    {
        return failure.FailureType == NetworkFailureType.OperationCancelled;
    }
}
