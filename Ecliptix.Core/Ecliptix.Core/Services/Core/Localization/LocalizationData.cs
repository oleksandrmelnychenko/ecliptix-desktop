using System.Collections.Frozen;
using System.Collections.Generic;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Core.Localization;

public static class LocalizationData
{
    public static readonly FrozenDictionary<string, string> EnglishStrings = new Dictionary<string, string>
    {
        [ErrorI18NKeys.VALIDATION] = "VALIDATION failed; please review the fields and try again.",
        [ErrorI18NKeys.MAX_ATTEMPTS] = "Maximum attempts reached. Please wait or contact support.",
        [ErrorI18NKeys.INVALID_MOBILE] = "Invalid mobile number format.",
        [ErrorI18NKeys.OTP_EXPIRED] = "Verification code expired. Request a new one.",
        [ErrorI18NKeys.NOT_FOUND] = "Requested resource not found.",
        [ErrorI18NKeys.ALREADY_EXISTS] = "Resource already exists.",
        [ErrorI18NKeys.UNAUTHENTICATED] = "Authentication required. Please sign in again.",
        [ErrorI18NKeys.PERMISSION_DENIED] = "You do not have permission to perform this action.",
        [ErrorI18NKeys.PRECONDITION_FAILED] = "Action cannot be completed in the current state.",
        [ErrorI18NKeys.CONFLICT] = "Operation conflicted with existing data. Refresh and retry.",
        [ErrorI18NKeys.RESOURCE_EXHAUSTED] = "Too many attempts. Please try again later.",
        [ErrorI18NKeys.SERVICE_UNAVAILABLE] = "Service temporarily unavailable. Try again shortly.",
        [ErrorI18NKeys.DEPENDENCY_UNAVAILABLE] = "Upstream dependency is unavailable. Please retry.",
        [ErrorI18NKeys.DEADLINE_EXCEEDED] = "Request timed out. Please try again.",
        [ErrorI18NKeys.CANCELLED] = "Request was cancelled.",
        [ErrorI18NKeys.INTERNAL] = "Something went wrong. Please try again later.",
        [ErrorI18NKeys.DATABASE_UNAVAILABLE] = "Database is unavailable. Please retry later.",
        [LocalizationKeys.Authentication.Error.NAVIGATION_FAILURE] = "Failed to navigate to main window",
        [LocalizationKeys.Authentication.Error.MEMBERSHIP_IDENTIFIER_REQUIRED] =
            "Membership identifier is required",
        [LocalizationKeys.Authentication.Error.SESSION_EXPIRED] =
            "Your session has expired. Please start the registration process again.",
        [LocalizationKeys.Authentication.Error.REGISTRATION_INCOMPLETE] =
            "Registration data is incomplete. Please restart the registration process.",
        [LocalizationKeys.Common.SERVER_UNAVAILABLE] = "Server is currently unavailable. Please try again later.",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.TITLE] = "Create Account",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.DESCRIPTION] =
            "Confirm your number. We'll text you a verification code.",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.HINT] = "Include country code",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.WATERMARK] = "Mobile Number",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.BUTTON] = "Continue",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.TITLE] = "Reset Secure Key",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.DESCRIPTION] =
            "Enter your mobile number to receive a verification code.",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.HINT] = "Include country code",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.WATERMARK] = "Mobile Number",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.BUTTON] = "Continue",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.TITLE] = "Verify Your Number",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.DESCRIPTION] =
            "Enter the code sent to your mobile.",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.HINT] = "Verification Code",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.ERROR_INVALID_CODE] =
            "Invalid code. Please try again.",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.BUTTON_VERIFY] = "Verify",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.BUTTON_RESEND] = "Resend Code",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.TITLE] = "Choose a Nickname",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.DESCRIPTION] = "This name will be visible to others.",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.HINT] = "Your nickname",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.WATERMARK] = "Nickname",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.BUTTON] = "Confirm",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.TITLE] = "Set Secure Key",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.DESCRIPTION] =
            "Use a strong, unique secure key for security.",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.SECURE_KEY_PLACEHOLDER] = "Secure Key",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.SECURE_KEY_HINT] =
            "8 chars, 1 upper and 1 number",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.VERIFY_SECURE_KEY_PLACEHOLDER] =
            "Confirm Secure Key",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.VERIFY_SECURE_KEY_HINT] =
            "Re-enter the secure key to confirm",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.ERROR_SECURE_KEY_MISMATCH] =
            "Secure keys do not match.",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.BUTTON] = "Set Secure Key",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.TITLE] = "Reset Secure Key",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.DESCRIPTION] =
            "Create a new secure key for your account.",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.NEW_SECURE_KEY_PLACEHOLDER] = "New Secure Key",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.NEW_SECURE_KEY_HINT] =
            "8 chars, 1 upper and 1 number",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.CONFIRM_SECURE_KEY_PLACEHOLDER] =
            "Confirm New Key",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.CONFIRM_SECURE_KEY_HINT] =
            "Re-enter your new secure key",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.BUTTON] = "Reset Secure Key",
        [LocalizationKeys.Authentication.SignUp.PassPhase.TITLE] = "Set PIN",
        [LocalizationKeys.Authentication.SignUp.PassPhase.DESCRIPTION] = "For quick and secure authentication.",
        [LocalizationKeys.Authentication.SignUp.PassPhase.HINT] = "Your PIN",
        [LocalizationKeys.Authentication.SignUp.PassPhase.WATERMARK] = "••••",
        [LocalizationKeys.Authentication.SignUp.PassPhase.BUTTON] = "Confirm PIN",
        [LocalizationKeys.Authentication.SignIn.TITLE] = "Sign In",
        [LocalizationKeys.Authentication.SignIn.WELCOME] =
            "Welcome back! Let's get you back to your secure, personalized workspace.",
        [LocalizationKeys.Authentication.SignIn.MOBILE_PLACEHOLDER] = "Mobile Number",
        [LocalizationKeys.Authentication.SignIn.MOBILE_HINT] = "Include country code (e.g., +1)",
        [LocalizationKeys.Authentication.SignIn.SECURE_KEY_PLACEHOLDER] = "Secure Key",
        [LocalizationKeys.Authentication.SignIn.SECURE_KEY_HINT] = "Stored only on this device",
        [LocalizationKeys.Authentication.SignIn.ACCOUNT_RECOVERY] = "Forgot key?",
        [LocalizationKeys.Authentication.SignIn.CONTINUE] = "Continue",
        [LocalizationKeys.MobileVerificationStatus.Error.MOBILE_ALREADY_REGISTERED] =
            "A problem occurred with your registration. If you already have an account, please try logging in or use the account recovery option.",
        [LocalizationKeys.MobileVerificationStatus.AVAILABLE_FOR_REGISTRATION] =
            "This mobile number is available for registration.",
        [LocalizationKeys.MobileVerificationStatus.INCOMPLETE_REGISTRATION_CONTINUE] =
            "Continue your registration with this mobile number.",
        [LocalizationKeys.MobileVerificationStatus.TAKEN_ACTIVE_ACCOUNT] =
            "This mobile number is already registered with an active account. Please sign in instead.",
        [LocalizationKeys.MobileVerificationStatus.TAKEN_INACTIVE_ACCOUNT] =
            "This mobile number is registered but inactive. Please use secure key recovery to regain access.",
        [LocalizationKeys.MobileVerificationStatus.DATA_CORRUPTION_CONTACT_SUPPORT] =
            "There's an issue with your account data. Please contact support for assistance.",
        [LocalizationKeys.MobileVerificationStatus.AVAILABLE_ON_THIS_DEVICE] =
            "This mobile number is available for registration on this device.",
        [LocalizationKeys.Verification.Error.INVALID_OTP_CODE] = "Invalid verification code",
        [LocalizationKeys.Registration.Error.FAILED] = "Registration failed",
        [LocalizationKeys.Verification.Error.NO_SESSION] = "Verification session not found",
        [LocalizationKeys.Verification.Error.SESSION_EXPIRED] = "Verification session has expired",
        [LocalizationKeys.Verification.Error.NO_ACTIVE_SESSION] = "No active verification session",
        [LocalizationKeys.Verification.Error.MAX_ATTEMPTS_REACHED] = "Maximum verification attempts reached",
        [LocalizationKeys.Verification.Error.VERIFICATION_FAILED] = "Verification failed",
        [LocalizationKeys.Verification.Error.SESSION_NOT_FOUND] = "Verification session not found",
        [LocalizationKeys.Verification.Error.GLOBAL_RATE_LIMIT_EXCEEDED] =
            "Too many requests. Please try again later",
        [LocalizationKeys.Verification.Info.REDIRECTING] = "Redirecting...",
        [LocalizationKeys.Verification.Info.REDIRECTING_IN_SECONDS] = "Redirecting in {0} seconds...",
        [LocalizationKeys.ValidationErrors.MobileNumber.MUST_START_WITH_COUNTRY_CODE] = "Must start with +",
        [LocalizationKeys.ValidationErrors.MobileNumber.CONTAINS_NON_DIGITS] = "Digits only after code",
        [LocalizationKeys.ValidationErrors.MobileNumber.INCORRECT_LENGTH] = "{0}-{1} digits required",
        [LocalizationKeys.ValidationErrors.MobileNumber.CANNOT_BE_EMPTY] = "REQUIRED",
        [LocalizationKeys.ResponseErrors.MobileNumber.ACCOUNT_ALREADY_REGISTERED] =
            "Account on this number already registered. Try sign in or use forgot secure key.",
        [LocalizationKeys.ResponseErrors.MobileNumber.UNEXPECTED_MEMBERSHIP_STATUS] =
            "Unexpected membership status. Please try again.",
        [LocalizationKeys.ResponseErrors.Common.TIMEOUT_EXCEEDED] = "The operation timed out. Please try again.",
        [LocalizationKeys.ValidationErrors.MobileNumber.REQUIRED] = "Mobile number is required",
        [LocalizationKeys.ValidationErrors.MobileNumberIdentifier.REQUIRED] =
            "Mobile number identifier is required",
        [LocalizationKeys.ValidationErrors.DeviceIdentifier.REQUIRED] = "Device identifier is required",
        [LocalizationKeys.ValidationErrors.SessionIdentifier.REQUIRED] = "Session identifier is required",
        [LocalizationKeys.ValidationErrors.MembershipIdentifier.REQUIRED] = "Membership identifier is required",
        [LocalizationKeys.ValidationErrors.SecureKey.REQUIRED] = "REQUIRED",
        [LocalizationKeys.ValidationErrors.SecureKey.MIN_LENGTH] = "Min {0} characters",
        [LocalizationKeys.ValidationErrors.SecureKey.MAX_LENGTH] = "Max {0} characters",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_SPACES] = "No spaces allowed",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_UPPERCASE] = "Requires an uppercase letter",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_LOWERCASE] = "Requires a lowercase letter",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_DIGIT] = "Requires a digit",
        [LocalizationKeys.ValidationErrors.SecureKey.TOO_SIMPLE] = "Try adding more variety",
        [LocalizationKeys.ValidationErrors.SecureKey.TOO_COMMON] = "Too common",
        [LocalizationKeys.ValidationErrors.SecureKey.SEQUENTIAL_PATTERN] = "No sequences (e.g., abc, 123)",
        [LocalizationKeys.ValidationErrors.SecureKey.REPEATED_CHARS] = "No repeats (e.g., aaa, 111)",
        [LocalizationKeys.ValidationErrors.SecureKey.LACKS_DIVERSITY] = "Requires {0} character types (A, a, 1, $)",
        [LocalizationKeys.ValidationErrors.SecureKey.CONTAINS_APP_NAME] = "Cannot contain app name",
        [LocalizationKeys.ValidationErrors.SecureKey.INVALID_CREDENTIALS] = "Invalid credentials",
        [LocalizationKeys.ValidationErrors.SecureKey.NON_ENGLISH_LETTERS] = "Latin letters only",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_SPECIAL_CHAR] = "Requires a special character",
        [LocalizationKeys.ValidationErrors.VerifySecureKey.DOES_NOT_MATCH] = "Secure keys do not match",
        [LocalizationKeys.ValidationErrors.Strength.INVALID] = "Invalid",
        [LocalizationKeys.ValidationErrors.Strength.VERY_WEAK] = "Very Weak",
        [LocalizationKeys.ValidationErrors.Strength.WEAK] = "Weak",
        [LocalizationKeys.ValidationErrors.Strength.GOOD] = "Good",
        [LocalizationKeys.ValidationErrors.Strength.STRONG] = "Strong",
        [LocalizationKeys.ValidationErrors.Strength.VERY_STRONG] = "Very Strong",
        [LocalizationKeys.ValidationWarnings.SecureKey.NON_LATIN_LETTER] = "Latin letters only",
        [LocalizationKeys.ValidationWarnings.SecureKey.INVALID_CHARACTER] = "Invalid character used",
        [LocalizationKeys.ValidationWarnings.SecureKey.MULTIPLE_CHARACTERS] =
            "Multiple instances of character type",
        [LocalizationKeys.Welcome.SIGN_IN_BUTTON] = "Sign In",
        [LocalizationKeys.Welcome.CREATE_ACCOUNT_BUTTON] = "Create Account",
        [LocalizationKeys.Footer.PRIVACY_POLICY] = "Privacy Policy",
        [LocalizationKeys.Footer.TERMS_OF_SERVICE] = "Terms of Service",
        [LocalizationKeys.Footer.SUPPORT] = "Support",
        [LocalizationKeys.Footer.AGREEMENT_TEXT] = "By continuing, you agree to our Terms and Privacy Policy.",
        [LocalizationKeys.Footer.COPYRIGHT] = "© 2025 Horizon Dynamics. All rights reserved.",
        [LocalizationKeys.Navigation.BACK] = "Back",
        [LocalizationKeys.Navigation.CLOSE] = "Close",
        [LocalizationKeys.Navigation.MINIMIZE] = "Minimize",
        [LocalizationKeys.Navigation.MAXIMIZE] = "Maximize",
        [LocalizationKeys.Common.LOADING] = "Loading...",
        [LocalizationKeys.Common.ERROR] = "ERROR",
        [LocalizationKeys.Common.SUCCESS] = "Success",
        [LocalizationKeys.Common.CANCEL] = "Cancel",
        [LocalizationKeys.Common.UNEXPECTED_ERROR] = "An unexpected error occurred",
        [LocalizationKeys.Common.OK] = "OK",
        [LocalizationKeys.Common.NO_INTERNET_CONNECTION] = "No internet connection",
        [LocalizationKeys.Common.CHECK_CONNECTION] = "Check your internet connection",
        [LocalizationKeys.NetworkNotification.NoInternet.TITLE] = "No Internet Connection",
        [LocalizationKeys.NetworkNotification.NoInternet.DESCRIPTION] = "Check your connection and try again",
        [LocalizationKeys.NetworkNotification.CheckingInternet.TITLE] = "Checking Connection",
        [LocalizationKeys.NetworkNotification.CheckingInternet.DESCRIPTION] = "Verifying internet connectivity",
        [LocalizationKeys.NetworkNotification.InternetRestored.TITLE] = "Connection Restored",
        [LocalizationKeys.NetworkNotification.InternetRestored.DESCRIPTION] = "Internet connection is back",
        [LocalizationKeys.NetworkNotification.Connecting.TITLE] = "Connecting",
        [LocalizationKeys.NetworkNotification.Connecting.DESCRIPTION] = "Establishing connection to server",
        [LocalizationKeys.NetworkNotification.Reconnecting.TITLE] = "Reconnecting",
        [LocalizationKeys.NetworkNotification.Reconnecting.DESCRIPTION] = "Attempting to reconnect to server",
        [LocalizationKeys.NetworkNotification.ServerNotResponding.TITLE] = "Server Unavailable",
        [LocalizationKeys.NetworkNotification.ServerNotResponding.DESCRIPTION] = "Our servers are not responding",
        [LocalizationKeys.NetworkNotification.ServerShuttingDown.TITLE] = "Server Maintenance",
        [LocalizationKeys.NetworkNotification.ServerShuttingDown.DESCRIPTION] = "Server is shutting down",
        [LocalizationKeys.NetworkNotification.RetriesExhausted.TITLE] = "Connection Failed",
        [LocalizationKeys.NetworkNotification.RetriesExhausted.DESCRIPTION] =
            "Unable to connect after multiple attempts",
        [LocalizationKeys.NetworkNotification.ServerReconnected.TITLE] = "Reconnected",
        [LocalizationKeys.NetworkNotification.ServerReconnected.DESCRIPTION] = "Successfully reconnected to server",
        [LocalizationKeys.NetworkNotification.Button.RETRY] = "Retry",
        [LocalizationKeys.LanguageDetection.TITLE] = "Language Suggestion",
        [LocalizationKeys.LanguageDetection.PROMPT] = "Switch to {0}?",
        [LocalizationKeys.LanguageDetection.BUTTON_CONFIRM] = "Switch Language",
        [LocalizationKeys.LanguageDetection.BUTTON_DECLINE] = "Not Now"
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, string> UkrainianStrings = new Dictionary<string, string>
    {
        [ErrorI18NKeys.VALIDATION] = "Перевірка не пройшла; перевірте дані та повторіть спробу.",
        [ErrorI18NKeys.MAX_ATTEMPTS] =
            "Досягнуто максимальну кількість спроб. Зачекайте або зверніться до підтримки.",
        [ErrorI18NKeys.INVALID_MOBILE] = "Неправильний формат номера телефону.",
        [ErrorI18NKeys.OTP_EXPIRED] = "Термін дії коду підтвердження минув. Запитайте новий.",
        [ErrorI18NKeys.NOT_FOUND] = "Запитаний ресурс не знайдено.",
        [ErrorI18NKeys.ALREADY_EXISTS] = "Такий ресурс уже існує.",
        [ErrorI18NKeys.UNAUTHENTICATED] = "Потрібна автентифікація. Увійдіть ще раз.",
        [ErrorI18NKeys.PERMISSION_DENIED] = "У вас немає дозволу на цю дію.",
        [ErrorI18NKeys.PRECONDITION_FAILED] = "Дію неможливо виконати в поточному стані.",
        [ErrorI18NKeys.CONFLICT] = "Операція конфліктує з наявними даними. Оновіть та повторіть.",
        [ErrorI18NKeys.RESOURCE_EXHAUSTED] = "Забагато спроб. Спробуйте пізніше.",
        [ErrorI18NKeys.SERVICE_UNAVAILABLE] = "Сервіс тимчасово недоступний. Спробуйте пізніше.",
        [ErrorI18NKeys.DEPENDENCY_UNAVAILABLE] = "Зовнішня залежність недоступна. Повторіть спробу.",
        [ErrorI18NKeys.DEADLINE_EXCEEDED] = "Час очікування запиту вичерпано. Спробуйте ще раз.",
        [ErrorI18NKeys.CANCELLED] = "Запит було скасовано.",
        [ErrorI18NKeys.INTERNAL] = "Сталася помилка. Спробуйте пізніше.",
        [ErrorI18NKeys.DATABASE_UNAVAILABLE] = "База даних недоступна. Повторіть спробу пізніше.",
        [LocalizationKeys.Common.SERVER_UNAVAILABLE] = "Сервер наразі недоступний. Будь ласка, спробуйте пізніше.",
        [LocalizationKeys.Authentication.Error.NAVIGATION_FAILURE] = "Не вдалося перейти до головного вікна",
        [LocalizationKeys.Authentication.Error.MEMBERSHIP_IDENTIFIER_REQUIRED] =
            "Ідентифікатор учасника є обов'язковим",
        [LocalizationKeys.Authentication.Error.SESSION_EXPIRED] =
            "Ваша сесія закінчилася. Будь ласка, почніть реєстрацію спочатку.",
        [LocalizationKeys.Authentication.Error.REGISTRATION_INCOMPLETE] =
            "Дані реєстрації неповні. Будь ласка, почніть процес реєстрації знову.",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.TITLE] = "Створити акаунт",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.DESCRIPTION] =
            "Підтвердьте номер. Ми надішлемо код в SMS.",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.HINT] = "Включно з кодом країни",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.WATERMARK] = "Номер мобільного",
        [LocalizationKeys.Authentication.SignUp.SignUpMobileVerification.BUTTON] = "Продовжити",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.TITLE] = "Скинути ключ безпеки",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.DESCRIPTION] =
            "Підтвердьте номер для отримання коду підтвердження.",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.HINT] = "Включно з кодом країни",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.WATERMARK] = "Номер мобільного",
        [LocalizationKeys.Authentication.SecureKeyRecovery.RecoveryMobileVerification.BUTTON] = "Продовжити",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.TITLE] = "Підтвердження номера",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.DESCRIPTION] =
            "Введіть код, надісланий на ваш мобільний.",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.HINT] = "Код підтвердження",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.ERROR_INVALID_CODE] =
            "Неправильний код. Спробуйте ще раз.",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.BUTTON_VERIFY] = "Підтвердити",
        [LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.BUTTON_RESEND] = "Надіслати знову",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.TITLE] = "Оберіть нікнейм",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.DESCRIPTION] = "Це ім'я буде видимим для інших.",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.HINT] = "Ваш нікнейм",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.WATERMARK] = "Нікнейм",
        [LocalizationKeys.Authentication.SignUp.NicknameInput.BUTTON] = "Підтвердити",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.TITLE] = "Встановіть ключ безпеки",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.DESCRIPTION] =
            "Для безпеки використовуйте надійний ключ безпеки.",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.SECURE_KEY_PLACEHOLDER] = "Ключ безпеки",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.SECURE_KEY_HINT] =
            "8 символів, 1 велика літера та 1 цифра",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.VERIFY_SECURE_KEY_PLACEHOLDER] =
            "Повторіть ключ",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.VERIFY_SECURE_KEY_HINT] =
            "Введіть ключ ще раз для підтвердження",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.ERROR_SECURE_KEY_MISMATCH] =
            "Ключі безпеки не збігаються.",
        [LocalizationKeys.Authentication.SignUp.SecureKeyConfirmation.BUTTON] = "Встановити ключ безпеки",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.TITLE] = "Скидання ключа безпеки",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.DESCRIPTION] =
            "Створіть новий ключ безпеки для облікового запису.",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.NEW_SECURE_KEY_PLACEHOLDER] = "Новий ключ безпеки",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.NEW_SECURE_KEY_HINT] =
            "8 символів, 1 велика літера та 1 цифра",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.CONFIRM_SECURE_KEY_PLACEHOLDER] =
            "Підтвердіть новий ключ",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.CONFIRM_SECURE_KEY_HINT] =
            "Введіть новий ключ ще раз",
        [LocalizationKeys.Authentication.SecureKeyRecovery.Reset.BUTTON] = "Скинути ключ безпеки",
        [LocalizationKeys.Authentication.SignUp.PassPhase.TITLE] = "Встановіть PIN-код",
        [LocalizationKeys.Authentication.SignUp.PassPhase.DESCRIPTION] = "Для швидкої та безпечної автентифікації.",
        [LocalizationKeys.Authentication.SignUp.PassPhase.HINT] = "Ваш PIN-код",
        [LocalizationKeys.Authentication.SignUp.PassPhase.WATERMARK] = "••••",
        [LocalizationKeys.Authentication.SignUp.PassPhase.BUTTON] = "Підтвердити PIN",
        [LocalizationKeys.Authentication.SignIn.TITLE] = "Вхід",
        [LocalizationKeys.Authentication.SignIn.WELCOME] = "З поверненням! Увійдіть у свій захищений простір.",
        [LocalizationKeys.Authentication.SignIn.MOBILE_PLACEHOLDER] = "Номер телефону",
        [LocalizationKeys.Authentication.SignIn.MOBILE_HINT] = "Почніть з коду країни (напр., +380)",
        [LocalizationKeys.Authentication.SignIn.SECURE_KEY_PLACEHOLDER] = "Ключ безпеки",
        [LocalizationKeys.Authentication.SignIn.SECURE_KEY_HINT] = "Зберігається лише на цьому пристрої",
        [LocalizationKeys.Authentication.SignIn.ACCOUNT_RECOVERY] = "Забули ключ?",
        [LocalizationKeys.Authentication.SignIn.CONTINUE] = "Продовжити",
        [LocalizationKeys.MobileVerificationStatus.Error.MOBILE_ALREADY_REGISTERED] =
            "Під час реєстрації сталася помилка. Якщо у вас вже є акаунт, будь ласка, спробуйте увійти або скористайтеся опцією відновлення доступу.",
        [LocalizationKeys.MobileVerificationStatus.AVAILABLE_FOR_REGISTRATION] = "Цей номер доступний для реєстрації.",
        [LocalizationKeys.MobileVerificationStatus.INCOMPLETE_REGISTRATION_CONTINUE] =
            "Продовжіть реєстрацію з цим номером.",
        [LocalizationKeys.MobileVerificationStatus.TAKEN_ACTIVE_ACCOUNT] =
            "Цей номер вже зареєстровано з активним акаунтом. Будь ласка, увійдіть.",
        [LocalizationKeys.MobileVerificationStatus.TAKEN_INACTIVE_ACCOUNT] =
            "Цей номер зареєстровано, але акаунт неактивний. Скористайтеся відновленням ключа безпеки.",
        [LocalizationKeys.MobileVerificationStatus.DATA_CORRUPTION_CONTACT_SUPPORT] =
            "Виникла проблема з даними вашого акаунта. Будь ласка, зверніться до підтримки.",
        [LocalizationKeys.MobileVerificationStatus.AVAILABLE_ON_THIS_DEVICE] =
            "Цей номер доступний для реєстрації на цьому пристрої.",
        [LocalizationKeys.Verification.Error.INVALID_OTP_CODE] = "Неправильний код підтвердження",
        [LocalizationKeys.Registration.Error.FAILED] = "Не вдалося зареєструватися",
        [LocalizationKeys.Verification.Error.NO_SESSION] = "Сесія підтвердження не знайдена",
        [LocalizationKeys.Verification.Error.SESSION_EXPIRED] = "Термін дії сесії підтвердження минув",
        [LocalizationKeys.Verification.Error.NO_ACTIVE_SESSION] = "Немає активної сесії підтвердження",
        [LocalizationKeys.Verification.Error.MAX_ATTEMPTS_REACHED] =
            "Досягнуто максимальну кількість спроб підтвердження",
        [LocalizationKeys.Verification.Error.VERIFICATION_FAILED] = "Підтвердження не вдалося",
        [LocalizationKeys.Verification.Error.SESSION_NOT_FOUND] = "Сесія підтвердження не знайдена",
        [LocalizationKeys.Verification.Error.GLOBAL_RATE_LIMIT_EXCEEDED] =
            "Занадто багато запитів. Спробуйте пізніше",
        [LocalizationKeys.Verification.Info.REDIRECTING] = "Перенаправлення...",
        [LocalizationKeys.Verification.Info.REDIRECTING_IN_SECONDS] = "Перенаправлення через {0} секунд...",
        [LocalizationKeys.ValidationErrors.MobileNumber.MUST_START_WITH_COUNTRY_CODE] = "Має починатись із +",
        [LocalizationKeys.ValidationErrors.MobileNumber.CONTAINS_NON_DIGITS] = "Лише цифри після коду",
        [LocalizationKeys.ValidationErrors.MobileNumber.INCORRECT_LENGTH] = "Потрібно {0}-{1} цифр",
        [LocalizationKeys.ValidationErrors.MobileNumber.CANNOT_BE_EMPTY] = "Обов'язкове поле",
        [LocalizationKeys.ValidationErrors.MobileNumber.REQUIRED] = "Номер мобільного телефону є обов'язковим",
        [LocalizationKeys.ValidationErrors.MobileNumberIdentifier.REQUIRED] =
            "Ідентифікатор номера мобільного телефону є обов'язковим",
        [LocalizationKeys.ValidationErrors.DeviceIdentifier.REQUIRED] = "Ідентифікатор пристрою є обов'язковим",
        [LocalizationKeys.ValidationErrors.SessionIdentifier.REQUIRED] = "Ідентифікатор сесії є обов'язковим",
        [LocalizationKeys.ValidationErrors.MembershipIdentifier.REQUIRED] = "Ідентифікатор учасника є обов'язковим",
        [LocalizationKeys.ResponseErrors.MobileNumber.ACCOUNT_ALREADY_REGISTERED] =
            "Акаунт з цим номером вже зареєстровано. Спробуйте увійти або скористайтеся відновленням пароля.",
        [LocalizationKeys.ResponseErrors.MobileNumber.UNEXPECTED_MEMBERSHIP_STATUS] =
            "Неочікуваний статус системи. Спробуйте ще раз.",
        [LocalizationKeys.ResponseErrors.Common.TIMEOUT_EXCEEDED] =
            "Операція перевищила час очікування. Спробуйте ще раз.",
        [LocalizationKeys.ValidationErrors.SecureKey.REQUIRED] = "Обов'язкове поле",
        [LocalizationKeys.ValidationErrors.SecureKey.MIN_LENGTH] = "Мін. {0} символів",
        [LocalizationKeys.ValidationErrors.SecureKey.MAX_LENGTH] = "Макс. {0} символів",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_SPACES] = "Без пробілів",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_UPPERCASE] = "Потрібна велика літера",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_LOWERCASE] = "Потрібна мала літера",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_DIGIT] = "Потрібна цифра",
        [LocalizationKeys.ValidationErrors.SecureKey.TOO_SIMPLE] = "Додайте більше різноманітності",
        [LocalizationKeys.ValidationErrors.SecureKey.TOO_COMMON] = "Занадто поширений",
        [LocalizationKeys.ValidationErrors.SecureKey.SEQUENTIAL_PATTERN] = "Без послідовностей (abc, 123)",
        [LocalizationKeys.ValidationErrors.SecureKey.REPEATED_CHARS] = "Без повторів (aaa, 111)",
        [LocalizationKeys.ValidationErrors.SecureKey.LACKS_DIVERSITY] = "Потрібно {0} типи символів (A, a, 1, $)",
        [LocalizationKeys.ValidationErrors.SecureKey.CONTAINS_APP_NAME] = "Не може містити назву додатку",
        [LocalizationKeys.ValidationErrors.SecureKey.INVALID_CREDENTIALS] = "Неправильні облікові дані",
        [LocalizationKeys.ValidationErrors.SecureKey.NON_ENGLISH_LETTERS] = "Лише латинські літери",
        [LocalizationKeys.ValidationErrors.SecureKey.NO_SPECIAL_CHAR] = "Потрібен спеціальний символ",
        [LocalizationKeys.ValidationErrors.VerifySecureKey.DOES_NOT_MATCH] = "Ключі безпеки не збігаються",
        [LocalizationKeys.ValidationErrors.Strength.INVALID] = "Некоректний",
        [LocalizationKeys.ValidationErrors.Strength.VERY_WEAK] = "Дуже слабкий",
        [LocalizationKeys.ValidationErrors.Strength.WEAK] = "Слабкий",
        [LocalizationKeys.ValidationErrors.Strength.GOOD] = "Хороший",
        [LocalizationKeys.ValidationErrors.Strength.STRONG] = "Сильний",
        [LocalizationKeys.ValidationErrors.Strength.VERY_STRONG] = "Дуже сильний",
        [LocalizationKeys.ValidationWarnings.SecureKey.NON_LATIN_LETTER] = "Лише латинські літери",
        [LocalizationKeys.ValidationWarnings.SecureKey.INVALID_CHARACTER] = "Використано недопустимий символ",
        [LocalizationKeys.ValidationWarnings.SecureKey.MULTIPLE_CHARACTERS] = "Кілька символів одного типу",
        [LocalizationKeys.Welcome.SIGN_IN_BUTTON] = "Увійти",
        [LocalizationKeys.Welcome.CREATE_ACCOUNT_BUTTON] = "Створити акаунт",
        [LocalizationKeys.Footer.PRIVACY_POLICY] = "Політика конфіденційності",
        [LocalizationKeys.Footer.TERMS_OF_SERVICE] = "Умови надання послуг",
        [LocalizationKeys.Footer.SUPPORT] = "Підтримка",
        [LocalizationKeys.Footer.AGREEMENT_TEXT] =
            "Продовжуючи, ви погоджуєтесь з нашими Умовами та Політикою конфіденційності.",
        [LocalizationKeys.Footer.COPYRIGHT] = "© 2025 Horizon Dynamics. Усі права захищено.",
        [LocalizationKeys.Navigation.BACK] = "Назад",
        [LocalizationKeys.Navigation.CLOSE] = "Закрити",
        [LocalizationKeys.Navigation.MINIMIZE] = "Згорнути",
        [LocalizationKeys.Navigation.MAXIMIZE] = "Розгорнути",
        [LocalizationKeys.Common.LOADING] = "Завантаження...",
        [LocalizationKeys.Common.ERROR] = "Помилка",
        [LocalizationKeys.Common.SUCCESS] = "Успішно",
        [LocalizationKeys.Common.CANCEL] = "Скасувати",
        [LocalizationKeys.Common.UNEXPECTED_ERROR] = "Несподівана помилка",
        [LocalizationKeys.Common.OK] = "Гаразд",
        [LocalizationKeys.Common.NO_INTERNET_CONNECTION] = "Немає підключення до інтернету",
        [LocalizationKeys.Common.CHECK_CONNECTION] = "Перевірте ваше інтернет-з'єднання",
        [LocalizationKeys.NetworkNotification.NoInternet.TITLE] = "Немає підключення до інтернету",
        [LocalizationKeys.NetworkNotification.NoInternet.DESCRIPTION] = "Перевірте з'єднання та спробуйте знову",
        [LocalizationKeys.NetworkNotification.CheckingInternet.TITLE] = "Перевірка з'єднання",
        [LocalizationKeys.NetworkNotification.CheckingInternet.DESCRIPTION] =
            "Перевіряємо підключення до інтернету",
        [LocalizationKeys.NetworkNotification.InternetRestored.TITLE] = "З'єднання відновлено",
        [LocalizationKeys.NetworkNotification.InternetRestored.DESCRIPTION] = "Підключення до інтернету відновлено",
        [LocalizationKeys.NetworkNotification.Connecting.TITLE] = "Підключення",
        [LocalizationKeys.NetworkNotification.Connecting.DESCRIPTION] = "Встановлюємо з'єднання з сервером",
        [LocalizationKeys.NetworkNotification.Reconnecting.TITLE] = "Перепідключення",
        [LocalizationKeys.NetworkNotification.Reconnecting.DESCRIPTION] =
            "Намагаємось відновити з'єднання з сервером",
        [LocalizationKeys.NetworkNotification.ServerNotResponding.TITLE] = "Сервер недоступний",
        [LocalizationKeys.NetworkNotification.ServerNotResponding.DESCRIPTION] = "Наші сервери не відповідають",
        [LocalizationKeys.NetworkNotification.ServerShuttingDown.TITLE] = "Обслуговування сервера",
        [LocalizationKeys.NetworkNotification.ServerShuttingDown.DESCRIPTION] = "Сервер вимикається",
        [LocalizationKeys.NetworkNotification.RetriesExhausted.TITLE] = "Не вдалось підключитись",
        [LocalizationKeys.NetworkNotification.RetriesExhausted.DESCRIPTION] =
            "Неможливо підключитись після кількох спроб",
        [LocalizationKeys.NetworkNotification.ServerReconnected.TITLE] = "Підключено",
        [LocalizationKeys.NetworkNotification.ServerReconnected.DESCRIPTION] = "Успішно підключено до сервера",
        [LocalizationKeys.NetworkNotification.Button.RETRY] = "Повторити",
        [LocalizationKeys.LanguageDetection.TITLE] = "Пропозиція мови",
        [LocalizationKeys.LanguageDetection.PROMPT] = "Перемкнутись на {0}?",
        [LocalizationKeys.LanguageDetection.BUTTON_CONFIRM] = "Змінити мову",
        [LocalizationKeys.LanguageDetection.BUTTON_DECLINE] = "Не зараз"
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, FrozenDictionary<string, string>> AllLanguages =
        new Dictionary<string, FrozenDictionary<string, string>>
        {
            ["en-US"] = EnglishStrings, ["uk-UA"] = UkrainianStrings
        }.ToFrozenDictionary();
}
