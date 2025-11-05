using System;

namespace Ecliptix.Core.Services.Authentication.Constants;

public static class AuthenticationConstants
{
    public const string MOBILE_NUMBER_REQUIRED_KEY = "ValidationErrors.MobileNumber.REQUIRED";
    public const string MOBILE_NUMBER_IDENTIFIER_REQUIRED_KEY = "ValidationErrors.MobileNumberIdentifier.REQUIRED";
    public const string SESSION_IDENTIFIER_REQUIRED_KEY = "ValidationErrors.SessionIdentifier.REQUIRED";
    public const string MEMBERSHIP_IDENTIFIER_REQUIRED_KEY = "ValidationErrors.MembershipIdentifier.REQUIRED";
    public const string SECURE_KEY_REQUIRED_KEY = "ValidationErrors.SecureKey.REQUIRED";
    public const string INVALID_CREDENTIALS_KEY = "ValidationErrors.SecureKey.InvalidCredentials";
    public const string VERIFY_SECURE_KEY_DOES_NOT_MATCH_KEY = "ValidationErrors.VerifySecureKey.DoesNotMatch";
    public const string COMMON_UNEXPECTED_ERROR_KEY = "Common.UnexpectedError";

    public const string SECURE_KEY_STRENGTH_INVALID_KEY = "ValidationErrors.SecureKeyStrength.Invalid";
    public const string SECURE_KEY_STRENGTH_VERY_WEAK_KEY = "ValidationErrors.SecureKeyStrength.VeryWeak";
    public const string SECURE_KEY_STRENGTH_WEAK_KEY = "ValidationErrors.SecureKeyStrength.Weak";
    public const string SECURE_KEY_STRENGTH_GOOD_KEY = "ValidationErrors.SecureKeyStrength.Good";
    public const string SECURE_KEY_STRENGTH_STRONG_KEY = "ValidationErrors.SecureKeyStrength.Strong";
    public const string SECURE_KEY_STRENGTH_VERY_STRONG_KEY = "ValidationErrors.SecureKeyStrength.VeryStrong";

    public const string NETWORK_FAILURE_PREFIX = "Failed to parse response: ";
    public const string VERIFICATION_FAILURE_PREFIX = "Verification failed: ";
    public const string REGISTRATION_FAILURE_PREFIX = "Registration failed: ";

    public const string INVALID_OTP_CODE_KEY = "Verification.ERROR.InvalidOtpCode";
    public const string REGISTRATION_FAILED_KEY = "Registration.ERROR.Failed";
    public const string NO_VERIFICATION_SESSION_KEY = "Verification.ERROR.NoSession";
    public const string VERIFICATION_SESSION_EXPIRED_KEY = "Verification.ERROR.SESSION_EXPIRED";
    public const string NO_ACTIVE_VERIFICATION_SESSION_KEY = "Verification.ERROR.NoActiveSession";
    public const string MAX_ATTEMPTS_REACHED_KEY = "Verification.ERROR.MaxAttemptsReached";
    public const string SESSION_NOT_FOUND_KEY = "Verification.ERROR.SessionNotFound";
    public const string REDIRECTING_IN_SECONDS_KEY = "Verification.Info.RedirectingInSeconds";
    public const string NAVIGATION_FAILURE_KEY = "Authentication.ERROR.NavigationFailure";

    public const string INITIAL_REMAINING_TIME = "00:30";
    public const string EXPIRED_REMAINING_TIME = "00:00";

    public static readonly Guid EmptyGuid = Guid.Empty;

    public static class ErrorMessages
    {
        public const string SESSION_EXPIRED_START_OVER = "Session expired. Please start over.";
    }

    public static class Timeouts
    {
        public static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    }

    public static class SecureKeyConfirmationKeys
    {
        public const string REGISTRATION_TITLE = "Authentication.SignUp.SecureKeyConfirmation.Title";
        public const string REGISTRATION_DESCRIPTION = "Authentication.SignUp.SecureKeyConfirmation.Description";
        public const string REGISTRATION_BUTTON = "Authentication.SignUp.SecureKeyConfirmation.Button";

        public const string RECOVERY_TITLE = "Authentication.SecureKeyRecovery.Reset.Title";
        public const string RECOVERY_DESCRIPTION = "Authentication.SecureKeyRecovery.Reset.Description";
        public const string RECOVERY_BUTTON = "Authentication.SecureKeyRecovery.Reset.Button";

        public const string SECURE_KEY_PLACEHOLDER = "Authentication.SignUp.SecureKeyConfirmation.SECURE_KEY_PLACEHOLDER";
        public const string SECURE_KEY_HINT = "Authentication.SignUp.SecureKeyConfirmation.SECURE_KEY_HINT";
        public const string VERIFY_SECURE_KEY_PLACEHOLDER = "Authentication.SignUp.SecureKeyConfirmation.VERIFY_SECURE_KEY_PLACEHOLDER";
        public const string VERIFY_SECURE_KEY_HINT = "Authentication.SignUp.SecureKeyConfirmation.VERIFY_SECURE_KEY_HINT";

        public const string RECOVERY_SECURE_KEY_PLACEHOLDER = "Authentication.SecureKeyRecovery.Reset.NewSecureKeyPlaceholder";
        public const string RECOVERY_SECURE_KEY_HINT = "Authentication.SecureKeyRecovery.Reset.NewSecureKeyHint";
        public const string RECOVERY_VERIFY_SECURE_KEY_PLACEHOLDER = "Authentication.SecureKeyRecovery.Reset.ConfirmSecureKeyPlaceholder";
        public const string RECOVERY_VERIFY_SECURE_KEY_HINT = "Authentication.SecureKeyRecovery.Reset.ConfirmSecureKeyHint";
    }

    public static class MobileVerificationKeys
    {
        public const string REGISTRATION_TITLE = "Authentication.SignUp.MobileVerification.Title";
        public const string REGISTRATION_DESCRIPTION = "Authentication.SignUp.MobileVerification.Description";
        public const string REGISTRATION_HINT = "Authentication.SignUp.MobileVerification.Hint";
        public const string REGISTRATION_WATERMARK = "Authentication.SignUp.MobileVerification.Watermark";
        public const string REGISTRATION_BUTTON = "Authentication.SignUp.MobileVerification.Button";

        public const string RECOVERY_TITLE = "Authentication.SecureKeyRecovery.MobileVerification.Title";
        public const string RECOVERY_DESCRIPTION = "Authentication.SecureKeyRecovery.MobileVerification.Description";
        public const string RECOVERY_HINT = "Authentication.SecureKeyRecovery.MobileVerification.Hint";
        public const string RECOVERY_WATERMARK = "Authentication.SecureKeyRecovery.MobileVerification.Watermark";
        public const string RECOVERY_BUTTON = "Authentication.SecureKeyRecovery.MobileVerification.Button";
    }


}
