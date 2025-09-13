using System;

namespace Ecliptix.Core.Services.Authentication.Constants;

public static class AuthenticationConstants
{
    public const string MobileNumberRequiredKey = "ValidationErrors.MobileNumber.Required";
    public const string PhoneNumberIdentifierRequiredKey = "ValidationErrors.PhoneNumberIdentifier.Required";
    public const string DeviceIdentifierRequiredKey = "ValidationErrors.DeviceIdentifier.Required";
    public const string SessionIdentifierRequiredKey = "ValidationErrors.SessionIdentifier.Required";
    public const string MembershipIdentifierRequiredKey = "ValidationErrors.MembershipIdentifier.Required";
    public const string SecureKeyRequiredKey = "ValidationErrors.SecureKey.Required";
    public const string InvalidCredentialsKey = "ValidationErrors.SecureKey.InvalidCredentials";
    public const string VerifySecureKeyDoesNotMatchKey = "ValidationErrors.VerifySecureKey.DoesNotMatch";

    public const string PasswordStrengthInvalidKey = "ValidationErrors.PasswordStrength.Invalid";
    public const string PasswordStrengthVeryWeakKey = "ValidationErrors.PasswordStrength.VeryWeak";
    public const string PasswordStrengthWeakKey = "ValidationErrors.PasswordStrength.Weak";
    public const string PasswordStrengthGoodKey = "ValidationErrors.PasswordStrength.Good";
    public const string PasswordStrengthStrongKey = "ValidationErrors.PasswordStrength.Strong";
    public const string PasswordStrengthVeryStrongKey = "ValidationErrors.PasswordStrength.VeryStrong";

    public const string NetworkFailurePrefix = "Failed to parse response: ";
    public const string VerificationFailurePrefix = "Verification failed: ";
    public const string RegistrationFailurePrefix = "Registration failed: ";

    public const string InvalidOtpCodeKey = "Verification.Error.InvalidOtpCode";
    public const string RegistrationFailedKey = "Registration.Error.Failed";
    public const string NoVerificationSessionKey = "Verification.Error.NoSession";
    public const string VerificationSessionExpiredKey = "Verification.Error.SessionExpired";
    public const string NoActiveVerificationSessionKey = "Verification.Error.NoActiveSession";
    public const string MaxAttemptsReachedKey = "Verification.Error.MaxAttemptsReached";
    public const string VerificationFailedKey = "Verification.Error.VerificationFailed";
    public const string SessionNotFoundKey = "Verification.Error.SessionNotFound";
    public const string RedirectingInSecondsKey = "Verification.Info.RedirectingInSeconds";
    public const string RedirectingMessageKey = "Verification.Info.Redirecting";

    public const string InitialRemainingTime = "00:30";
    public const string ExpiredRemainingTime = "00:00";

    public static readonly Guid EmptyGuid = Guid.Empty;

    public static class Timeouts
    {
        public static readonly TimeSpan DefaultCleanupTimeout = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan VerificationCleanupTimeout = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan TaskWaitTimeout = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan StreamInitializationTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan OtpVerificationTimeout = TimeSpan.FromSeconds(15);
        public static readonly TimeSpan PhoneValidationTimeout = TimeSpan.FromSeconds(10);
        public static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(3);
    }
}