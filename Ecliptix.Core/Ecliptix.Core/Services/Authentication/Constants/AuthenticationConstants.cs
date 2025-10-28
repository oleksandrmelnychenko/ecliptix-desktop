using System;

namespace Ecliptix.Core.Services.Authentication.Constants;

public static class AuthenticationConstants
{
    public const string MobileNumberRequiredKey = "ValidationErrors.MobileNumber.Required";
    public const string MobileNumberIdentifierRequiredKey = "ValidationErrors.MobileNumberIdentifier.Required";
    public const string DeviceIdentifierRequiredKey = "ValidationErrors.DeviceIdentifier.Required";
    public const string SessionIdentifierRequiredKey = "ValidationErrors.SessionIdentifier.Required";
    public const string MembershipIdentifierRequiredKey = "ValidationErrors.MembershipIdentifier.Required";
    public const string SecureKeyRequiredKey = "ValidationErrors.SecureKey.Required";
    public const string InvalidCredentialsKey = "ValidationErrors.SecureKey.InvalidCredentials";
    public const string VerifySecureKeyDoesNotMatchKey = "ValidationErrors.VerifySecureKey.DoesNotMatch";
    public const string CommonUnexpectedErrorKey = "Common.UnexpectedError";

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
    public const string SessionNotFoundKey = "Verification.Error.SessionNotFound";
    public const string RedirectingInSecondsKey = "Verification.Info.RedirectingInSeconds";
    public const string NavigationFailureKey = "Authentication.Error.NavigationFailure";


    public const string AccountAlreadyExistsKey = "ResponseErrors.MobileNumber.AccountAlreadyRegistered";
    public const string TimeoutExceededKey = "ResponseErrors.Common.TimeoutExceeded";

    public const string InitialRemainingTime = "00:30";
    public const string ExpiredRemainingTime = "00:00";

    public static readonly Guid EmptyGuid = Guid.Empty;

    public static class ErrorMessages
    {
        public const string SessionNotFound = "Session not found";
        public const string StartOver = "start over";
        public const string SessionExpiredStartOver = "Session expired. Please start over.";
    }

    public static class Timeouts
    {
        public static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    }

    public static class SecureKeyConfirmationKeys
    {
        public const string RegistrationTitle = "Authentication.SignUp.PasswordConfirmation.Title";
        public const string RegistrationDescription = "Authentication.SignUp.PasswordConfirmation.Description";
        public const string RegistrationButton = "Authentication.SignUp.PasswordConfirmation.Button";

        public const string RecoveryTitle = "Authentication.PasswordRecovery.Reset.Title";
        public const string RecoveryDescription = "Authentication.PasswordRecovery.Reset.Description";
        public const string RecoveryButton = "Authentication.PasswordRecovery.Reset.Button";

        public const string PasswordPlaceholder = "Authentication.SignUp.PasswordConfirmation.PasswordPlaceholder";
        public const string PasswordHint = "Authentication.SignUp.PasswordConfirmation.PasswordHint";
        public const string VerifyPasswordPlaceholder = "Authentication.SignUp.PasswordConfirmation.VerifyPasswordPlaceholder";
        public const string VerifyPasswordHint = "Authentication.SignUp.PasswordConfirmation.VerifyPasswordHint";

        public const string RecoveryPasswordPlaceholder = "Authentication.PasswordRecovery.Reset.NewPasswordPlaceholder";
        public const string RecoveryPasswordHint = "Authentication.PasswordRecovery.Reset.NewPasswordHint";
        public const string RecoveryVerifyPasswordPlaceholder = "Authentication.PasswordRecovery.Reset.ConfirmPasswordPlaceholder";
        public const string RecoveryVerifyPasswordHint = "Authentication.PasswordRecovery.Reset.ConfirmPasswordHint";
    }

    public static class MobileVerificationKeys
    {
        public const string RegistrationTitle = "Authentication.SignUp.MobileVerification.Title";
        public const string RegistrationDescription = "Authentication.SignUp.MobileVerification.Description";
        public const string RegistrationHint = "Authentication.SignUp.MobileVerification.Hint";
        public const string RegistrationWatermark = "Authentication.SignUp.MobileVerification.Watermark";
        public const string RegistrationButton = "Authentication.SignUp.MobileVerification.Button";

        public const string RecoveryTitle = "Authentication.PasswordRecovery.MobileVerification.Title";
        public const string RecoveryDescription = "Authentication.PasswordRecovery.MobileVerification.Description";
        public const string RecoveryHint = "Authentication.PasswordRecovery.MobileVerification.Hint";
        public const string RecoveryWatermark = "Authentication.PasswordRecovery.MobileVerification.Watermark";
        public const string RecoveryButton = "Authentication.PasswordRecovery.MobileVerification.Button";
    }


}
