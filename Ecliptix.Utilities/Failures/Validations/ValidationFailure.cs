using Grpc.Core;

namespace Ecliptix.Utilities.Failures.Validations;

public enum ValidationFailureType
{
    SIGN_IN_FAILED,
    LOGIN_ATTEMPT_EXCEEDED
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
        new(ValidationFailureType.SIGN_IN_FAILED, details, inner);

    public static ValidationFailure LoginAttemptExceeded(string details, Exception? inner = null) =>
        new(ValidationFailureType.LOGIN_ATTEMPT_EXCEEDED, details, inner);

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            ValidationFailureType.LOGIN_ATTEMPT_EXCEEDED => new GrpcErrorDescriptor(
                ErrorCode.MAX_ATTEMPTS_REACHED, StatusCode.ResourceExhausted, ErrorI18NKeys.MAX_ATTEMPTS),
            _ => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION)
        };
}
