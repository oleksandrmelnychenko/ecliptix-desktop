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
}
