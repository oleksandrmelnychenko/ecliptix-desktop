using Grpc.Core;

namespace Ecliptix.Utilities.Failures.Validations;

public enum ValidationFailureType
{
    SignInFailed,
    LoginAttemptExceeded
}

public record ValidationFailure(
    ValidationFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public override object ToStructuredLog()
    {
        return new
        {
            ValidationFailureType = FailureType.ToString(),
            Message,
            InnerException,
            Timestamp
        };
    }

    public static ValidationFailure SignInFailed(string details, Exception? inner = null) =>
        new(ValidationFailureType.SignInFailed, details, inner);

    public static ValidationFailure LoginAttemptExceeded(string details, Exception? inner = null) =>
        new(ValidationFailureType.LoginAttemptExceeded, details, inner);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            ValidationFailureType.LoginAttemptExceeded => new GrpcErrorDescriptor(
                ErrorCode.MaxAttemptsReached, StatusCode.ResourceExhausted, ErrorI18nKeys.MaxAttempts),
            _ => new GrpcErrorDescriptor(
                ErrorCode.ValidationFailed, StatusCode.InvalidArgument, ErrorI18nKeys.Validation)
        };
}
