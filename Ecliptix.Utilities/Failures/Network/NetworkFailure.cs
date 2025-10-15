namespace Ecliptix.Utilities.Failures.Network;

public record NetworkFailure(
    NetworkFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public override object ToStructuredLog()
    {
        return new
        {
            NetworkFailureType = FailureType.ToString(),
            Message,
            InnerException,
            Timestamp
        };
    }

    public static NetworkFailure InvalidRequestType(string details, Exception? inner = null) =>
        new(NetworkFailureType.InvalidRequestType, details, inner);

    public static NetworkFailure DataCenterNotResponding(string details, Exception? inner = null) =>
        new(NetworkFailureType.DataCenterNotResponding, details, inner);

    public static NetworkFailure DataCenterShutdown(string details, Exception? inner = null) =>
        new(NetworkFailureType.DataCenterShutdown, details, inner);

    public static NetworkFailure RsaEncryption(string details, Exception? inner = null) =>
        new(NetworkFailureType.RsaEncryptionFailure, details, inner);

    public static NetworkFailure ProtocolStateMismatch(string details, Exception? inner = null) =>
        new(NetworkFailureType.ProtocolStateMismatch, details, inner);

    public static NetworkFailure OperationCancelled(string? details = null, Exception? inner = null) =>
        new(NetworkFailureType.OperationCancelled, details ?? "Operation was cancelled", inner);
}
