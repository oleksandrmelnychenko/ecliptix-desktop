namespace Ecliptix.Core.Services.Core.Localization;

public static class LocalizationKeys
{
    public static class Authentication
    {
        public static class Error
        {
            public const string NAVIGATION_FAILURE = "Authentication.ERROR.NavigationFailure";
            public const string MEMBERSHIP_IDENTIFIER_REQUIRED = "Authentication.ERROR.MembershipIdentifierRequired";
            public const string SESSION_EXPIRED = "Authentication.ERROR.SESSION_EXPIRED";
            public const string REGISTRATION_INCOMPLETE = "Authentication.ERROR.RegistrationIncomplete";
        }

        public static class SignUp
        {
            public static class SignUpMobileVerification
            {
                public const string TITLE = "Authentication.SignUp.MobileVerification.Title";
                public const string DESCRIPTION = "Authentication.SignUp.MobileVerification.Description";
                public const string HINT = "Authentication.SignUp.MobileVerification.Hint";
                public const string WATERMARK = "Authentication.SignUp.MobileVerification.Watermark";
                public const string BUTTON = "Authentication.SignUp.MobileVerification.Button";
            }

            public static class VerificationCodeEntry
            {
                public const string TITLE = "Authentication.SignUp.VerificationCodeEntry.Title";
                public const string DESCRIPTION = "Authentication.SignUp.VerificationCodeEntry.Description";
                public const string HINT = "Authentication.SignUp.VerificationCodeEntry.Hint";
                public const string ERROR_INVALID_CODE = "Authentication.SignUp.VerificationCodeEntry.Error_InvalidCode";
                public const string BUTTON_VERIFY = "Authentication.SignUp.VerificationCodeEntry.Button.Verify";
                public const string BUTTON_RESEND = "Authentication.SignUp.VerificationCodeEntry.Button.Resend";
            }

            public static class NicknameInput
            {
                public const string TITLE = "Authentication.SignUp.NicknameInput.Title";
                public const string DESCRIPTION = "Authentication.SignUp.NicknameInput.Description";
                public const string HINT = "Authentication.SignUp.NicknameInput.Hint";
                public const string WATERMARK = "Authentication.SignUp.NicknameInput.Watermark";
                public const string BUTTON = "Authentication.SignUp.NicknameInput.Button";
            }

            public static class SecureKeyConfirmation
            {
                public const string TITLE = "Authentication.SignUp.SecureKeyConfirmation.Title";
                public const string DESCRIPTION = "Authentication.SignUp.SecureKeyConfirmation.Description";
                public const string SECURE_KEY_PLACEHOLDER = "Authentication.SignUp.SecureKeyConfirmation.SECURE_KEY_PLACEHOLDER";
                public const string SECURE_KEY_HINT = "Authentication.SignUp.SecureKeyConfirmation.SECURE_KEY_HINT";
                public const string VERIFY_SECURE_KEY_PLACEHOLDER = "Authentication.SignUp.SecureKeyConfirmation.VERIFY_SECURE_KEY_PLACEHOLDER";
                public const string VERIFY_SECURE_KEY_HINT = "Authentication.SignUp.SecureKeyConfirmation.VERIFY_SECURE_KEY_HINT";
                public const string ERROR_SECURE_KEY_MISMATCH = "Authentication.SignUp.SecureKeyConfirmation.Error_SecureKeyMismatch";
                public const string BUTTON = "Authentication.SignUp.SecureKeyConfirmation.Button";
            }

            public static class PassPhase
            {
                public const string TITLE = "Authentication.SignUp.PassPhase.Title";
                public const string DESCRIPTION = "Authentication.SignUp.PassPhase.Description";
                public const string HINT = "Authentication.SignUp.PassPhase.Hint";
                public const string WATERMARK = "Authentication.SignUp.PassPhase.Watermark";
                public const string BUTTON = "Authentication.SignUp.PassPhase.Button";
            }
        }

        public static class SecureKeyRecovery
        {
            public static class RecoveryMobileVerification
            {
                public const string TITLE = "Authentication.SecureKeyRecovery.MobileVerification.Title";
                public const string DESCRIPTION = "Authentication.SecureKeyRecovery.MobileVerification.Description";
                public const string HINT = "Authentication.SecureKeyRecovery.MobileVerification.Hint";
                public const string WATERMARK = "Authentication.SecureKeyRecovery.MobileVerification.Watermark";
                public const string BUTTON = "Authentication.SecureKeyRecovery.MobileVerification.Button";
            }

            public static class Reset
            {
                public const string TITLE = "Authentication.SecureKeyRecovery.Reset.Title";
                public const string DESCRIPTION = "Authentication.SecureKeyRecovery.Reset.Description";
                public const string NEW_SECURE_KEY_PLACEHOLDER = "Authentication.SecureKeyRecovery.Reset.NewSecureKeyPlaceholder";
                public const string NEW_SECURE_KEY_HINT = "Authentication.SecureKeyRecovery.Reset.NewSecureKeyHint";
                public const string CONFIRM_SECURE_KEY_PLACEHOLDER = "Authentication.SecureKeyRecovery.Reset.ConfirmSecureKeyPlaceholder";
                public const string CONFIRM_SECURE_KEY_HINT = "Authentication.SecureKeyRecovery.Reset.ConfirmSecureKeyHint";
                public const string BUTTON = "Authentication.SecureKeyRecovery.Reset.Button";
            }
        }

        public static class SignIn
        {
            public const string TITLE = "Authentication.SignIn.Title";
            public const string WELCOME = "Authentication.SignIn.Welcome";
            public const string MOBILE_PLACEHOLDER = "Authentication.SignIn.MobilePlaceholder";
            public const string MOBILE_HINT = "Authentication.SignIn.MobileHint";
            public const string SECURE_KEY_PLACEHOLDER = "Authentication.SignIn.SecureKeyPlaceholder";
            public const string SECURE_KEY_HINT = "Authentication.SignIn.SecureKeyHint";
            public const string ACCOUNT_RECOVERY = "Authentication.SignIn.AccountRecovery";
            public const string CONTINUE = "Authentication.SignIn.Continue";
        }
    }

    public static class MobileVerificationStatus
    {
        public static class Error
        {
            public const string MOBILE_ALREADY_REGISTERED = "MobileVerification.ERROR.MobileAlreadyRegistered";
        }

        public const string AVAILABLE_FOR_REGISTRATION = "mobile_available_for_registration";
        public const string INCOMPLETE_REGISTRATION_CONTINUE = "mobile_incomplete_registration_continue";
        public const string TAKEN_ACTIVE_ACCOUNT = "mobile_taken_active_account";
        public const string TAKEN_INACTIVE_ACCOUNT = "mobile_taken_inactive_account";
        public const string DATA_CORRUPTION_CONTACT_SUPPORT = "mobile_data_corruption_contact_support";
        public const string AVAILABLE_ON_THIS_DEVICE = "mobile_available_on_this_device";
    }

    public static class Verification
    {
        public static class Error
        {
            public const string INVALID_OTP_CODE = "Verification.ERROR.InvalidOtpCode";
            public const string NO_SESSION = "Verification.ERROR.NoSession";
            public const string SESSION_EXPIRED = "Verification.ERROR.SESSION_EXPIRED";
            public const string NO_ACTIVE_SESSION = "Verification.ERROR.NoActiveSession";
            public const string MAX_ATTEMPTS_REACHED = "Verification.ERROR.MaxAttemptsReached";
            public const string VERIFICATION_FAILED = "Verification.ERROR.VerificationFailed";
            public const string SESSION_NOT_FOUND = "Verification.ERROR.SessionNotFound";
            public const string GLOBAL_RATE_LIMIT_EXCEEDED = "Verification.ERROR.GlobalRateLimitExceeded";
        }

        public static class Info
        {
            public const string REDIRECTING = "Verification.Info.Redirecting";
            public const string REDIRECTING_IN_SECONDS = "Verification.Info.RedirectingInSeconds";
        }
    }

    public static class Registration
    {
        public static class Error
        {
            public const string FAILED = "Registration.ERROR.Failed";
        }
    }

    public static class ValidationErrors
    {
        public static class MobileNumber
        {
            public const string MUST_START_WITH_COUNTRY_CODE = "ValidationErrors.MobileNumber.MUST_START_WITH_COUNTRY_CODE";
            public const string CONTAINS_NON_DIGITS = "ValidationErrors.MobileNumber.CONTAINS_NON_DIGITS";
            public const string INCORRECT_LENGTH = "ValidationErrors.MobileNumber.INCORRECT_LENGTH";
            public const string CANNOT_BE_EMPTY = "ValidationErrors.MobileNumber.CANNOT_BE_EMPTY";
            public const string REQUIRED = "ValidationErrors.MobileNumber.REQUIRED";
        }

        public static class MobileNumberIdentifier
        {
            public const string REQUIRED = "ValidationErrors.MobileNumberIdentifier.REQUIRED";
        }

        public static class DeviceIdentifier
        {
            public const string REQUIRED = "ValidationErrors.DeviceIdentifier.REQUIRED";
        }

        public static class SessionIdentifier
        {
            public const string REQUIRED = "ValidationErrors.SessionIdentifier.REQUIRED";
        }

        public static class MembershipIdentifier
        {
            public const string REQUIRED = "ValidationErrors.MembershipIdentifier.REQUIRED";
        }

        public static class SecureKey
        {
            public const string REQUIRED = "ValidationErrors.SecureKey.REQUIRED";
            public const string MIN_LENGTH = "ValidationErrors.SecureKey.MIN_LENGTH";
            public const string MAX_LENGTH = "ValidationErrors.SecureKey.MAX_LENGTH";
            public const string NO_SPACES = "ValidationErrors.SecureKey.NO_SPACES";
            public const string NO_UPPERCASE = "ValidationErrors.SecureKey.NO_UPPERCASE";
            public const string NO_LOWERCASE = "ValidationErrors.SecureKey.NO_LOWERCASE";
            public const string NO_DIGIT = "ValidationErrors.SecureKey.NO_DIGIT";
            public const string TOO_SIMPLE = "ValidationErrors.SecureKey.TOO_SIMPLE";
            public const string TOO_COMMON = "ValidationErrors.SecureKey.TOO_COMMON";
            public const string SEQUENTIAL_PATTERN = "ValidationErrors.SecureKey.SEQUENTIAL_PATTERN";
            public const string REPEATED_CHARS = "ValidationErrors.SecureKey.REPEATED_CHARS";
            public const string LACKS_DIVERSITY = "ValidationErrors.SecureKey.LACKS_DIVERSITY";
            public const string CONTAINS_APP_NAME = "ValidationErrors.SecureKey.CONTAINS_APP_NAME";
            public const string INVALID_CREDENTIALS = "ValidationErrors.SecureKey.InvalidCredentials";
            public const string NON_ENGLISH_LETTERS = "ValidationErrors.SecureKey.NON_ENGLISH_LETTERS";
            public const string NO_SPECIAL_CHAR = "ValidationErrors.SecureKey.NO_SPECIAL_CHAR";
        }

        public static class VerifySecureKey
        {
            public const string DOES_NOT_MATCH = "ValidationErrors.VerifySecureKey.DoesNotMatch";
        }

        public static class Strength
        {
            public const string INVALID = "ValidationErrors.SecureKeyStrength.Invalid";
            public const string VERY_WEAK = "ValidationErrors.SecureKeyStrength.VeryWeak";
            public const string WEAK = "ValidationErrors.SecureKeyStrength.Weak";
            public const string GOOD = "ValidationErrors.SecureKeyStrength.Good";
            public const string STRONG = "ValidationErrors.SecureKeyStrength.Strong";
            public const string VERY_STRONG = "ValidationErrors.SecureKeyStrength.VeryStrong";
        }
    }

    public static class ValidationWarnings
    {
        public static class SecureKey
        {
            public const string NON_LATIN_LETTER = "ValidationWarnings.SecureKey.NonLatinLetter";
            public const string INVALID_CHARACTER = "ValidationWarnings.SecureKey.InvalidCharacter";
            public const string MULTIPLE_CHARACTERS = "ValidationWarnings.SecureKey.MultipleCharacters";
        }
    }

    public static class ResponseErrors
    {
        public static class MobileNumber
        {
            public const string ACCOUNT_ALREADY_REGISTERED = "ResponseErrors.MobileNumber.AccountAlreadyRegistered";
            public const string UNEXPECTED_MEMBERSHIP_STATUS = "ResponseErrors.MobileNumber.UnexpectedMembershipStatus";
        }

        public static class General
        {
            public const string TIMEOUT_EXCEEDED = "ResponseErrors.Common.TimeoutExceeded";
        }
    }

    public static class Welcome
    {
        public const string SIGN_IN_BUTTON = "Welcome.SignInButton";
        public const string CREATE_ACCOUNT_BUTTON = "Welcome.CreateAccountButton";
    }

    public static class Footer
    {
        public const string PRIVACY_POLICY = "Footer.PrivacyPolicy";
        public const string TERMS_OF_SERVICE = "Footer.TermsOfService";
        public const string SUPPORT = "Footer.Support";
        public const string AGREEMENT_TEXT = "Footer.AgreementText";
        public const string COPYRIGHT = "Footer.Copyright";
    }

    public static class Navigation
    {
        public const string BACK = "Navigation.Back";
        public const string CLOSE = "Navigation.Close";
        public const string MINIMIZE = "Navigation.Minimize";
        public const string MAXIMIZE = "Navigation.Maximize";
    }

    public static class Common
    {
        public const string LOADING = "Common.Loading";
        public const string ERROR = "Common.ERROR";
        public const string SUCCESS = "Common.Success";
        public const string CANCEL = "Common.Cancel";
        public const string UNEXPECTED_ERROR = "Common.UnexpectedError";
        public const string OK = "Common.Ok";
        public const string NO_INTERNET_CONNECTION = "Common.NoInternetConnection";
        public const string CHECK_CONNECTION = "Common.CheckConnection";
        public const string SERVER_UNAVAILABLE = "error.server_unavailable";
    }

    public static class NetworkNotification
    {
        public static class NoInternet
        {
            public const string TITLE = "NetworkNotification.NoInternet.Title";
            public const string DESCRIPTION = "NetworkNotification.NoInternet.Description";
        }

        public static class CheckingInternet
        {
            public const string TITLE = "NetworkNotification.CheckingInternet.Title";
            public const string DESCRIPTION = "NetworkNotification.CheckingInternet.Description";
        }

        public static class InternetRestored
        {
            public const string TITLE = "NetworkNotification.InternetRestored.Title";
            public const string DESCRIPTION = "NetworkNotification.InternetRestored.Description";
        }

        public static class Connecting
        {
            public const string TITLE = "NetworkNotification.Connecting.Title";
            public const string DESCRIPTION = "NetworkNotification.Connecting.Description";
        }

        public static class Reconnecting
        {
            public const string TITLE = "NetworkNotification.Reconnecting.Title";
            public const string DESCRIPTION = "NetworkNotification.Reconnecting.Description";
        }

        public static class ServerNotResponding
        {
            public const string TITLE = "NetworkNotification.ServerNotResponding.Title";
            public const string DESCRIPTION = "NetworkNotification.ServerNotResponding.Description";
        }

        public static class ServerShuttingDown
        {
            public const string TITLE = "NetworkNotification.ServerShuttingDown.Title";
            public const string DESCRIPTION = "NetworkNotification.ServerShuttingDown.Description";
        }

        public static class RetriesExhausted
        {
            public const string TITLE = "NetworkNotification.RetriesExhausted.Title";
            public const string DESCRIPTION = "NetworkNotification.RetriesExhausted.Description";
        }

        public static class ServerReconnected
        {
            public const string TITLE = "NetworkNotification.ServerReconnected.Title";
            public const string DESCRIPTION = "NetworkNotification.ServerReconnected.Description";
        }

        public static class Button
        {
            public const string RETRY = "NetworkNotification.Button.Retry";
        }
    }

    public static class LanguageDetection
    {
        public const string TITLE = "LanguageDetection.Title";
        public const string PROMPT = "LanguageDetection.Prompt";
        public const string BUTTON_CONFIRM = "LanguageDetection.Button.Confirm";
        public const string BUTTON_DECLINE = "LanguageDetection.Button.Decline";
    }
}
