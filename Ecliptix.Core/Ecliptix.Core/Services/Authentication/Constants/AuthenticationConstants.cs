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

    public const string SecureKeyStrengthInvalidKey = "ValidationErrors.SecureKeyStrength.Invalid";
    public const string SecureKeyStrengthVeryWeakKey = "ValidationErrors.SecureKeyStrength.VeryWeak";
    public const string SecureKeyStrengthWeakKey = "ValidationErrors.SecureKeyStrength.Weak";
    public const string SecureKeyStrengthGoodKey = "ValidationErrors.SecureKeyStrength.Good";
    public const string SecureKeyStrengthStrongKey = "ValidationErrors.SecureKeyStrength.Strong";
    public const string SecureKeyStrengthVeryStrongKey = "ValidationErrors.SecureKeyStrength.VeryStrong";

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
        public const string RegistrationTitle = "Authentication.SignUp.SecureKeyConfirmation.Title";
        public const string RegistrationDescription = "Authentication.SignUp.SecureKeyConfirmation.Description";
        public const string RegistrationButton = "Authentication.SignUp.SecureKeyConfirmation.Button";

        public const string RecoveryTitle = "Authentication.SecureKeyRecovery.Reset.Title";
        public const string RecoveryDescription = "Authentication.SecureKeyRecovery.Reset.Description";
        public const string RecoveryButton = "Authentication.SecureKeyRecovery.Reset.Button";

        public const string SecureKeyPlaceholder = "Authentication.SignUp.SecureKeyConfirmation.SecureKeyPlaceholder";
        public const string SecureKeyHint = "Authentication.SignUp.SecureKeyConfirmation.SecureKeyHint";
        public const string VerifySecureKeyPlaceholder = "Authentication.SignUp.SecureKeyConfirmation.VerifySecureKeyPlaceholder";
        public const string VerifySecureKeyHint = "Authentication.SignUp.SecureKeyConfirmation.VerifySecureKeyHint";

        public const string RecoverySecureKeyPlaceholder = "Authentication.SecureKeyRecovery.Reset.NewSecureKeyPlaceholder";
        public const string RecoverySecureKeyHint = "Authentication.SecureKeyRecovery.Reset.NewSecureKeyHint";
        public const string RecoveryVerifySecureKeyPlaceholder = "Authentication.SecureKeyRecovery.Reset.ConfirmSecureKeyPlaceholder";
        public const string RecoveryVerifySecureKeyHint = "Authentication.SecureKeyRecovery.Reset.ConfirmSecureKeyHint";
    }

    public static class MobileVerificationKeys
    {
        public const string RegistrationTitle = "Authentication.SignUp.MobileVerification.Title";
        public const string RegistrationDescription = "Authentication.SignUp.MobileVerification.Description";
        public const string RegistrationHint = "Authentication.SignUp.MobileVerification.Hint";
        public const string RegistrationWatermark = "Authentication.SignUp.MobileVerification.Watermark";
        public const string RegistrationButton = "Authentication.SignUp.MobileVerification.Button";

        public const string RecoveryTitle = "Authentication.SecureKeyRecovery.MobileVerification.Title";
        public const string RecoveryDescription = "Authentication.SecureKeyRecovery.MobileVerification.Description";
        public const string RecoveryHint = "Authentication.SecureKeyRecovery.MobileVerification.Hint";
        public const string RecoveryWatermark = "Authentication.SecureKeyRecovery.MobileVerification.Watermark";
        public const string RecoveryButton = "Authentication.SecureKeyRecovery.MobileVerification.Button";
    }


}
