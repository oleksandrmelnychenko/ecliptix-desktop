using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Network.Resilience;

public static class FailureClassification
{
    public static bool IsTransient(NetworkFailure failure)
    {
        if (failure.FailureType == NetworkFailureType.OPERATION_CANCELLED)
        {
            return false;
        }

        if (failure.FailureType == NetworkFailureType.PROTOCOL_STATE_MISMATCH)
        {
            return false;
        }

        if (failure.UserError?.Retryable is { } retryable)
        {
            return retryable;
        }

        return failure.FailureType == NetworkFailureType.DATA_CENTER_NOT_RESPONDING;
    }

    public static bool IsServerShutdown(NetworkFailure failure) => failure.FailureType == NetworkFailureType.DATA_CENTER_SHUTDOWN;

    public static bool IsCryptoDesync(NetworkFailure failure) => failure.FailureType == NetworkFailureType.RSA_ENCRYPTION_FAILURE;

    public static bool IsProtocolStateMismatch(NetworkFailure failure) => failure.FailureType == NetworkFailureType.PROTOCOL_STATE_MISMATCH;

    public static bool IsChainRotationMismatch(NetworkFailure failure) => IsProtocolStateMismatch(failure);

    public static bool IsCancellation(NetworkFailure failure) => failure.FailureType == NetworkFailureType.OPERATION_CANCELLED;
}
