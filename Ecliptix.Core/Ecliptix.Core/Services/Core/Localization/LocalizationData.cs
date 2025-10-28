using System.Collections.Frozen;
using System.Collections.Generic;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Core.Localization;

public static class LocalizationData
{
    public static readonly FrozenDictionary<string, string> EnglishStrings = new Dictionary<string, string>
    {
        [ErrorI18nKeys.Validation] = "Validation failed; please review the fields and try again.",
        [ErrorI18nKeys.MaxAttempts] = "Maximum attempts reached. Please wait or contact support.",
        [ErrorI18nKeys.InvalidMobile] = "Invalid mobile number format.",
        [ErrorI18nKeys.OtpExpired] = "Verification code expired. Request a new one.",
        [ErrorI18nKeys.NotFound] = "Requested resource not found.",
        [ErrorI18nKeys.AlreadyExists] = "Resource already exists.",
        [ErrorI18nKeys.Unauthenticated] = "Authentication required. Please sign in again.",
        [ErrorI18nKeys.PermissionDenied] = "You do not have permission to perform this action.",
        [ErrorI18nKeys.PreconditionFailed] = "Action cannot be completed in the current state.",
        [ErrorI18nKeys.Conflict] = "Operation conflicted with existing data. Refresh and retry.",
        [ErrorI18nKeys.ResourceExhausted] = "Too many attempts. Please try again later.",
        [ErrorI18nKeys.ServiceUnavailable] = "Service temporarily unavailable. Try again shortly.",
        [ErrorI18nKeys.DependencyUnavailable] = "Upstream dependency is unavailable. Please retry.",
        [ErrorI18nKeys.DeadlineExceeded] = "Request timed out. Please try again.",
        [ErrorI18nKeys.Cancelled] = "Request was cancelled.",
        [ErrorI18nKeys.Internal] = "Something went wrong. Please try again later.",
        [ErrorI18nKeys.DatabaseUnavailable] = "Database is unavailable. Please retry later.",


        ["Authentication.Error.NavigationFailure"] = "Failed to navigate to main window",
        ["Authentication.Error.MembershipIdentifierRequired"] = "Membership identifier is required",
        ["Authentication.Error.SessionExpired"] = "Your session has expired. Please start the registration process again.",
        ["Authentication.Error.RegistrationIncomplete"] = "Registration data is incomplete. Please restart the registration process.",


        ["error.server_unavailable"] = "Server is currently unavailable. Please try again later.",
        ["Authentication.SignUp.MobileVerification.Title"] = "Create Account",
        ["Authentication.SignUp.MobileVerification.Description"] = "Confirm your number. We'll text you a verification code.",
        ["Authentication.SignUp.MobileVerification.Hint"] = "Include country code",
        ["Authentication.SignUp.MobileVerification.Watermark"] = "Mobile Number",
        ["Authentication.SignUp.MobileVerification.Button"] = "Continue",


        ["Authentication.PasswordRecovery.MobileVerification.Title"] = "Reset Password",
        ["Authentication.PasswordRecovery.MobileVerification.Description"] = "Enter your mobile number to receive a verification code.",
        ["Authentication.PasswordRecovery.MobileVerification.Hint"] = "Include country code",
        ["Authentication.PasswordRecovery.MobileVerification.Watermark"] = "Mobile Number",
        ["Authentication.PasswordRecovery.MobileVerification.Button"] = "Continue",


        ["Authentication.SignUp.VerificationCodeEntry.Title"] = "Verify Your Number",
        ["Authentication.SignUp.VerificationCodeEntry.Description"] = "Enter the code sent to your mobile.",
        ["Authentication.SignUp.VerificationCodeEntry.Hint"] = "Verification Code",
        ["Authentication.SignUp.VerificationCodeEntry.Error_InvalidCode"] = "Invalid code. Please try again.",
        ["Authentication.SignUp.VerificationCodeEntry.Button.Verify"] = "Verify",
        ["Authentication.SignUp.VerificationCodeEntry.Button.Resend"] = "Resend Code",

        ["Authentication.SignUp.NicknameInput.Title"] = "Choose a Nickname",
        ["Authentication.SignUp.NicknameInput.Description"] = "This name will be visible to others.",
        ["Authentication.SignUp.NicknameInput.Hint"] = "Your nickname",
        ["Authentication.SignUp.NicknameInput.Watermark"] = "Nickname",
        ["Authentication.SignUp.NicknameInput.Button"] = "Confirm",

        ["Authentication.SignUp.PasswordConfirmation.Title"] = "Set Password",
        ["Authentication.SignUp.PasswordConfirmation.Description"] = "Use a strong, unique password for security.",
        ["Authentication.SignUp.PasswordConfirmation.PasswordPlaceholder"] = "Secure Key",
        ["Authentication.SignUp.PasswordConfirmation.PasswordHint"] = "8 chars, 1 upper and 1 number",
        ["Authentication.SignUp.PasswordConfirmation.VerifyPasswordPlaceholder"] = "Confirm Secure Key",
        ["Authentication.SignUp.PasswordConfirmation.VerifyPasswordHint"] = "Re-enter the secure key to confirm",
        ["Authentication.SignUp.PasswordConfirmation.Error_PasswordMismatch"] = "Passwords do not match.",
        ["Authentication.SignUp.PasswordConfirmation.Button"] = "Set Password",

        ["Authentication.PasswordRecovery.Reset.Title"] = "Reset Password",
        ["Authentication.PasswordRecovery.Reset.Description"] = "Create a new secure password for your account.",
        ["Authentication.PasswordRecovery.Reset.NewPasswordPlaceholder"] = "New Secure Key",
        ["Authentication.PasswordRecovery.Reset.NewPasswordHint"] = "8 chars, 1 upper and 1 number",
        ["Authentication.PasswordRecovery.Reset.ConfirmPasswordPlaceholder"] = "Confirm New Key",
        ["Authentication.PasswordRecovery.Reset.ConfirmPasswordHint"] = "Re-enter your new secure key",
        ["Authentication.PasswordRecovery.Reset.Button"] = "Reset Password",

        ["Authentication.SignUp.PassPhase.Title"] = "Set PIN",
        ["Authentication.SignUp.PassPhase.Description"] = "For quick and secure authentication.",
        ["Authentication.SignUp.PassPhase.Hint"] = "Your PIN",
        ["Authentication.SignUp.PassPhase.Watermark"] = "••••",
        ["Authentication.SignUp.PassPhase.Button"] = "Confirm PIN",


        ["Authentication.SignIn.Title"] = "Sign In",
        ["Authentication.SignIn.Welcome"] = "Welcome back! Let's get you back to your secure, personalized workspace.",
        ["Authentication.SignIn.MobilePlaceholder"] = "Mobile Number",
        ["Authentication.SignIn.MobileHint"] = "Include country code (e.g., +1)",
        ["Authentication.SignIn.PasswordPlaceholder"] = "Secure Key",
        ["Authentication.SignIn.PasswordHint"] = "Stored only on this device",
        ["Authentication.SignIn.AccountRecovery"] = "Forgot key?",
        ["Authentication.SignIn.Continue"] = "Continue",


        ["MobileVerification.Error.MobileAlreadyRegistered"] = "A problem occurred with your registration. If you already have an account, please try logging in or use the account recovery option.",
        ["mobile_available_for_registration"] = "This mobile number is available for registration.",
        ["mobile_incomplete_registration_continue"] = "Continue your registration with this mobile number.",
        ["mobile_taken_active_account"] = "This mobile number is already registered with an active account. Please sign in instead.",
        ["mobile_taken_inactive_account"] = "This mobile number is registered but inactive. Please use password recovery to regain access.",
        ["mobile_data_corruption_contact_support"] = "There's an issue with your account data. Please contact support for assistance.",
        ["mobile_available_on_this_device"] = "This mobile number is available for registration on this device.",


        ["Verification.Error.InvalidOtpCode"] = "Invalid verification code",
        ["Registration.Error.Failed"] = "Registration failed",
        ["Verification.Error.NoSession"] = "Verification session not found",
        ["Verification.Error.SessionExpired"] = "Verification session has expired",
        ["Verification.Error.NoActiveSession"] = "No active verification session",

        ["Verification.Error.MaxAttemptsReached"] = "Maximum verification attempts reached",
        ["Verification.Error.VerificationFailed"] = "Verification failed",
        ["Verification.Error.SessionNotFound"] = "Verification session not found",
        ["Verification.Error.GlobalRateLimitExceeded"] = "Too many requests. Please try again later",
        ["Verification.Info.Redirecting"] = "Redirecting...",
        ["Verification.Info.RedirectingInSeconds"] = "Redirecting in {0} seconds...",


        ["ValidationErrors.MobileNumber.MustStartWithCountryCode"] = "Must start with +",
        ["ValidationErrors.MobileNumber.ContainsNonDigits"] = "Digits only after code",
        ["ValidationErrors.MobileNumber.IncorrectLength"] = "{0}-{1} digits required",
        ["ValidationErrors.MobileNumber.CannotBeEmpty"] = "Required",

        ["ResponseErrors.MobileNumber.AccountAlreadyRegistered"] = "Account on this number already registered. Try sign in or use forgot password.",
        ["ResponseErrors.MobileNumber.UnexpectedMembershipStatus"] = "Unexpected membership status. Please try again.",
        ["ResponseErrors.Common.TimeoutExceeded"] = "The operation timed out. Please try again.",

        ["ValidationErrors.MobileNumber.Required"] = "Mobile number is required",
        ["ValidationErrors.MobileNumberIdentifier.Required"] = "Mobile number identifier is required",
        ["ValidationErrors.DeviceIdentifier.Required"] = "Device identifier is required",
        ["ValidationErrors.SessionIdentifier.Required"] = "Session identifier is required",
        ["ValidationErrors.MembershipIdentifier.Required"] = "Membership identifier is required",


        ["ValidationErrors.SecureKey.Required"] = "Required",
        ["ValidationErrors.SecureKey.MinLength"] = "Min {0} characters",
        ["ValidationErrors.SecureKey.MaxLength"] = "Max {0} characters",
        ["ValidationErrors.SecureKey.NoSpaces"] = "No spaces allowed",
        ["ValidationErrors.SecureKey.NoUppercase"] = "Requires an uppercase letter",
        ["ValidationErrors.SecureKey.NoLowercase"] = "Requires a lowercase letter",
        ["ValidationErrors.SecureKey.NoDigit"] = "Requires a digit",
        ["ValidationErrors.SecureKey.TooSimple"] = "Try adding more variety",
        ["ValidationErrors.SecureKey.TooCommon"] = "Too common",
        ["ValidationErrors.SecureKey.SequentialPattern"] = "No sequences (e.g., abc, 123)",
        ["ValidationErrors.SecureKey.RepeatedChars"] = "No repeats (e.g., aaa, 111)",
        ["ValidationErrors.SecureKey.LacksDiversity"] = "Requires {0} character types (A, a, 1, $)",
        ["ValidationErrors.SecureKey.ContainsAppName"] = "Cannot contain app name",
        ["ValidationErrors.SecureKey.InvalidCredentials"] = "Invalid credentials",
        ["ValidationErrors.SecureKey.NonEnglishLetters"] = "Latin letters only",
        ["ValidationErrors.SecureKey.NoSpecialChar"] = "Requires a special character",

        ["ValidationErrors.VerifySecureKey.DoesNotMatch"] = "Passwords do not match",

        ["ValidationErrors.PasswordStrength.Invalid"] = "Invalid",
        ["ValidationErrors.PasswordStrength.VeryWeak"] = "Very Weak",
        ["ValidationErrors.PasswordStrength.Weak"] = "Weak",
        ["ValidationErrors.PasswordStrength.Good"] = "Good",
        ["ValidationErrors.PasswordStrength.Strong"] = "Strong",
        ["ValidationErrors.PasswordStrength.VeryStrong"] = "Very Strong",

        ["ValidationWarnings.SecureKey.NonLatinLetter"] = "Latin letters only",
        ["ValidationWarnings.SecureKey.InvalidCharacter"] = "Invalid character used",
        ["ValidationWarnings.SecureKey.MultipleCharacters"] = "Multiple instances of character type",

        ["Welcome.SignInButton"] = "Sign In",
        ["Welcome.CreateAccountButton"] = "Create Account",


        ["Footer.PrivacyPolicy"] = "Privacy Policy",
        ["Footer.TermsOfService"] = "Terms of Service",
        ["Footer.Support"] = "Support",
        ["Footer.AgreementText"] = "By continuing, you agree to our Terms and Privacy Policy.",
        ["Footer.Copyright"] = "© 2025 Horizon Dynamics. All rights reserved.",


        ["Navigation.Back"] = "Back",
        ["Navigation.Close"] = "Close",
        ["Navigation.Minimize"] = "Minimize",
        ["Navigation.Maximize"] = "Maximize",


        ["Common.Loading"] = "Loading...",
        ["Common.Error"] = "Error",
        ["Common.Success"] = "Success",
        ["Common.Cancel"] = "Cancel",
        ["Common.UnexpectedError"] = "An unexpected error occurred",
        ["Common.Ok"] = "OK",
        ["Common.NoInternetConnection"] = "No internet connection",
        ["Common.CheckConnection"] = "Check your internet connection",


        ["NetworkNotification.NoInternet.Title"] = "No Internet Connection",
        ["NetworkNotification.NoInternet.Description"] = "Check your connection and try again",
        ["NetworkNotification.CheckingInternet.Title"] = "Checking Connection",
        ["NetworkNotification.CheckingInternet.Description"] = "Verifying internet connectivity",
        ["NetworkNotification.InternetRestored.Title"] = "Connection Restored",
        ["NetworkNotification.InternetRestored.Description"] = "Internet connection is back",
        ["NetworkNotification.Connecting.Title"] = "Connecting",
        ["NetworkNotification.Connecting.Description"] = "Establishing connection to server",
        ["NetworkNotification.Reconnecting.Title"] = "Reconnecting",
        ["NetworkNotification.Reconnecting.Description"] = "Attempting to reconnect to server",
        ["NetworkNotification.ServerNotResponding.Title"] = "Server Unavailable",
        ["NetworkNotification.ServerNotResponding.Description"] = "Our servers are not responding",
        ["NetworkNotification.ServerShuttingDown.Title"] = "Server Maintenance",
        ["NetworkNotification.ServerShuttingDown.Description"] = "Server is shutting down",
        ["NetworkNotification.RetriesExhausted.Title"] = "Connection Failed",
        ["NetworkNotification.RetriesExhausted.Description"] = "Unable to connect after multiple attempts",
        ["NetworkNotification.ServerReconnected.Title"] = "Reconnected",
        ["NetworkNotification.ServerReconnected.Description"] = "Successfully reconnected to server",
        ["NetworkNotification.Button.Retry"] = "Retry",


        ["LanguageDetection.Title"] = "Language Suggestion",
        ["LanguageDetection.Prompt"] = "Switch to {0}?",
        ["LanguageDetection.Button.Confirm"] = "Switch Language",
        ["LanguageDetection.Button.Decline"] = "Not Now"
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, string> UkrainianStrings = new Dictionary<string, string>
    {
        [ErrorI18nKeys.Validation] = "Перевірка не пройшла; перевірте дані та повторіть спробу.",
        [ErrorI18nKeys.MaxAttempts] = "Досягнуто максимальну кількість спроб. Зачекайте або зверніться до підтримки.",
        [ErrorI18nKeys.InvalidMobile] = "Неправильний формат номера телефону.",
        [ErrorI18nKeys.OtpExpired] = "Термін дії коду підтвердження минув. Запитайте новий.",
        [ErrorI18nKeys.NotFound] = "Запитаний ресурс не знайдено.",
        [ErrorI18nKeys.AlreadyExists] = "Такий ресурс уже існує.",
        [ErrorI18nKeys.Unauthenticated] = "Потрібна автентифікація. Увійдіть ще раз.",
        [ErrorI18nKeys.PermissionDenied] = "У вас немає дозволу на цю дію.",
        [ErrorI18nKeys.PreconditionFailed] = "Дію неможливо виконати в поточному стані.",
        [ErrorI18nKeys.Conflict] = "Операція конфліктує з наявними даними. Оновіть та повторіть.",
        [ErrorI18nKeys.ResourceExhausted] = "Забагато спроб. Спробуйте пізніше.",
        [ErrorI18nKeys.ServiceUnavailable] = "Сервіс тимчасово недоступний. Спробуйте пізніше.",
        [ErrorI18nKeys.DependencyUnavailable] = "Зовнішня залежність недоступна. Повторіть спробу.",
        [ErrorI18nKeys.DeadlineExceeded] = "Час очікування запиту вичерпано. Спробуйте ще раз.",
        [ErrorI18nKeys.Cancelled] = "Запит було скасовано.",
        [ErrorI18nKeys.Internal] = "Сталася помилка. Спробуйте пізніше.",
        [ErrorI18nKeys.DatabaseUnavailable] = "База даних недоступна. Повторіть спробу пізніше.",
        ["error.server_unavailable"] = "Сервер наразі недоступний. Будь ласка, спробуйте пізніше.",


        ["Authentication.Error.NavigationFailure"] = "Не вдалося перейти до головного вікна",
        ["Authentication.Error.MembershipIdentifierRequired"] = "Ідентифікатор учасника є обов'язковим",
        ["Authentication.Error.SessionExpired"] = "Ваша сесія закінчилася. Будь ласка, почніть реєстрацію спочатку.",
        ["Authentication.Error.RegistrationIncomplete"] = "Дані реєстрації неповні. Будь ласка, почніть процес реєстрації знову.",


        ["Authentication.SignUp.MobileVerification.Title"] = "Створити акаунт",
        ["Authentication.SignUp.MobileVerification.Description"] = "Підтвердьте номер. Ми надішлемо код в SMS.",
        ["Authentication.SignUp.MobileVerification.Hint"] = "Включно з кодом країни",
        ["Authentication.SignUp.MobileVerification.Watermark"] = "Номер мобільного",
        ["Authentication.SignUp.MobileVerification.Button"] = "Продовжити",


        ["Authentication.PasswordRecovery.MobileVerification.Title"] = "Скинути пароль",
        ["Authentication.PasswordRecovery.MobileVerification.Description"] = "Підтвердьте номер для отримання коду підтвердження.",
        ["Authentication.PasswordRecovery.MobileVerification.Hint"] = "Включно з кодом країни",
        ["Authentication.PasswordRecovery.MobileVerification.Watermark"] = "Номер мобільного",
        ["Authentication.PasswordRecovery.MobileVerification.Button"] = "Продовжити",


        ["Authentication.SignUp.VerificationCodeEntry.Title"] = "Підтвердження номера",
        ["Authentication.SignUp.VerificationCodeEntry.Description"] = "Введіть код, надісланий на ваш мобільний.",
        ["Authentication.SignUp.VerificationCodeEntry.Hint"] = "Код підтвердження",
        ["Authentication.SignUp.VerificationCodeEntry.Error_InvalidCode"] = "Неправильний код. Спробуйте ще раз.",
        ["Authentication.SignUp.VerificationCodeEntry.Button.Verify"] = "Підтвердити",
        ["Authentication.SignUp.VerificationCodeEntry.Button.Resend"] = "Надіслати знову",

        ["Authentication.SignUp.NicknameInput.Title"] = "Оберіть нікнейм",
        ["Authentication.SignUp.NicknameInput.Description"] = "Це ім'я буде видимим для інших.",
        ["Authentication.SignUp.NicknameInput.Hint"] = "Ваш нікнейм",
        ["Authentication.SignUp.NicknameInput.Watermark"] = "Нікнейм",
        ["Authentication.SignUp.NicknameInput.Button"] = "Підтвердити",

        ["Authentication.SignUp.PasswordConfirmation.Title"] = "Встановіть пароль",
        ["Authentication.SignUp.PasswordConfirmation.Description"] = "Для безпеки використовуйте надійний пароль.",
        ["Authentication.SignUp.PasswordConfirmation.PasswordPlaceholder"] = "Ключ безпеки",
        ["Authentication.SignUp.PasswordConfirmation.PasswordHint"] = "8 символів, 1 велика літера та 1 цифра",
        ["Authentication.SignUp.PasswordConfirmation.VerifyPasswordPlaceholder"] = "Повторіть ключ",
        ["Authentication.SignUp.PasswordConfirmation.VerifyPasswordHint"] = "Введіть ключ ще раз для підтвердження",
        ["Authentication.SignUp.PasswordConfirmation.Error_PasswordMismatch"] = "Паролі не збігаються.",
        ["Authentication.SignUp.PasswordConfirmation.Button"] = "Встановити пароль",

        ["Authentication.PasswordRecovery.Reset.Title"] = "Скидання пароля",
        ["Authentication.PasswordRecovery.Reset.Description"] = "Створіть новий безпечний пароль для облікового запису.",
        ["Authentication.PasswordRecovery.Reset.NewPasswordPlaceholder"] = "Новий ключ безпеки",
        ["Authentication.PasswordRecovery.Reset.NewPasswordHint"] = "8 символів, 1 велика літера та 1 цифра",
        ["Authentication.PasswordRecovery.Reset.ConfirmPasswordPlaceholder"] = "Підтвердіть новий ключ",
        ["Authentication.PasswordRecovery.Reset.ConfirmPasswordHint"] = "Введіть новий ключ ще раз",
        ["Authentication.PasswordRecovery.Reset.Button"] = "Скинути пароль",

        ["Authentication.SignUp.PassPhase.Title"] = "Встановіть PIN-код",
        ["Authentication.SignUp.PassPhase.Description"] = "Для швидкої та безпечної автентифікації.",
        ["Authentication.SignUp.PassPhase.Hint"] = "Ваш PIN-код",
        ["Authentication.SignUp.PassPhase.Watermark"] = "••••",
        ["Authentication.SignUp.PassPhase.Button"] = "Підтвердити PIN",


        ["Authentication.SignIn.Title"] = "Вхід",
        ["Authentication.SignIn.Welcome"] = "З поверненням! Увійдіть у свій захищений простір.",
        ["Authentication.SignIn.MobilePlaceholder"] = "Номер телефону",
        ["Authentication.SignIn.MobileHint"] = "Почніть з коду країни (напр., +380)",
        ["Authentication.SignIn.PasswordPlaceholder"] = "Ключ безпеки",
        ["Authentication.SignIn.PasswordHint"] = "Зберігається лише на цьому пристрої",
        ["Authentication.SignIn.AccountRecovery"] = "Забули ключ?",
        ["Authentication.SignIn.Continue"] = "Продовжити",


        ["MobileVerification.Error.MobileAlreadyRegistered"] = "Під час реєстрації сталася помилка. Якщо у вас вже є акаунт, будь ласка, спробуйте увійти або скористайтеся опцією відновлення доступу.",
        ["mobile_available_for_registration"] = "Цей номер доступний для реєстрації.",
        ["mobile_incomplete_registration_continue"] = "Продовжіть реєстрацію з цим номером.",
        ["mobile_taken_active_account"] = "Цей номер вже зареєстровано з активним акаунтом. Будь ласка, увійдіть.",
        ["mobile_taken_inactive_account"] = "Цей номер зареєстровано, але акаунт неактивний. Скористайтеся відновленням пароля.",
        ["mobile_data_corruption_contact_support"] = "Виникла проблема з даними вашого акаунта. Будь ласка, зверніться до підтримки.",
        ["mobile_available_on_this_device"] = "Цей номер доступний для реєстрації на цьому пристрої.",


        ["Verification.Error.InvalidOtpCode"] = "Неправильний код підтвердження",
        ["Registration.Error.Failed"] = "Не вдалося зареєструватися",
        ["Verification.Error.NoSession"] = "Сесія підтвердження не знайдена",
        ["Verification.Error.SessionExpired"] = "Термін дії сесії підтвердження минув",
        ["Verification.Error.NoActiveSession"] = "Немає активної сесії підтвердження",

        ["Verification.Error.MaxAttemptsReached"] = "Досягнуто максимальну кількість спроб підтвердження",
        ["Verification.Error.VerificationFailed"] = "Підтвердження не вдалося",
        ["Verification.Error.SessionNotFound"] = "Сесія підтвердження не знайдена",
        ["Verification.Error.GlobalRateLimitExceeded"] = "Занадто багато запитів. Спробуйте пізніше",
        ["Verification.Info.Redirecting"] = "Перенаправлення...",
        ["Verification.Info.RedirectingInSeconds"] = "Перенаправлення через {0} секунд...",


        ["ValidationErrors.MobileNumber.MustStartWithCountryCode"] = "Має починатись із +",
        ["ValidationErrors.MobileNumber.ContainsNonDigits"] = "Лише цифри після коду",
        ["ValidationErrors.MobileNumber.IncorrectLength"] = "Потрібно {0}-{1} цифр",
        ["ValidationErrors.MobileNumber.CannotBeEmpty"] = "Обов'язкове поле",


        ["ValidationErrors.MobileNumber.Required"] = "Номер мобільного телефону є обов'язковим",
        ["ValidationErrors.MobileNumberIdentifier.Required"] = "Ідентифікатор номера мобільного телефону є обов'язковим",
        ["ValidationErrors.DeviceIdentifier.Required"] = "Ідентифікатор пристрою є обов'язковим",
        ["ValidationErrors.SessionIdentifier.Required"] = "Ідентифікатор сесії є обов'язковим",
        ["ValidationErrors.MembershipIdentifier.Required"] = "Ідентифікатор учасника є обов'язковим",

        ["ResponseErrors.MobileNumber.AccountAlreadyRegistered"] = "Акаунт з цим номером вже зареєстровано. Спробуйте увійти або скористайтеся відновленням пароля.",
        ["ResponseErrors.MobileNumber.UnexpectedMembershipStatus"] = "Неочікуваний статус системи. Спробуйте ще раз.",
        ["ResponseErrors.Common.TimeoutExceeded"] = "Операція перевищила час очікування. Спробуйте ще раз.",


        ["ValidationErrors.SecureKey.Required"] = "Обов'язкове поле",
        ["ValidationErrors.SecureKey.MinLength"] = "Мін. {0} символів",
        ["ValidationErrors.SecureKey.MaxLength"] = "Макс. {0} символів",
        ["ValidationErrors.SecureKey.NoSpaces"] = "Без пробілів",
        ["ValidationErrors.SecureKey.NoUppercase"] = "Потрібна велика літера",
        ["ValidationErrors.SecureKey.NoLowercase"] = "Потрібна мала літера",
        ["ValidationErrors.SecureKey.NoDigit"] = "Потрібна цифра",
        ["ValidationErrors.SecureKey.TooSimple"] = "Додайте більше різноманітності",
        ["ValidationErrors.SecureKey.TooCommon"] = "Занадто поширений",
        ["ValidationErrors.SecureKey.SequentialPattern"] = "Без послідовностей (abc, 123)",
        ["ValidationErrors.SecureKey.RepeatedChars"] = "Без повторів (aaa, 111)",
        ["ValidationErrors.SecureKey.LacksDiversity"] = "Потрібно {0} типи символів (A, a, 1, $)",
        ["ValidationErrors.SecureKey.ContainsAppName"] = "Не може містити назву додатку",
        ["ValidationErrors.SecureKey.InvalidCredentials"] = "Неправильні облікові дані",
        ["ValidationErrors.SecureKey.NonEnglishLetters"] = "Лише латинські літери",
        ["ValidationErrors.SecureKey.NoSpecialChar"] = "Потрібен спеціальний символ",

        ["ValidationErrors.VerifySecureKey.DoesNotMatch"] = "Паролі не збігаються",

        ["ValidationErrors.PasswordStrength.Invalid"] = "Некоректний",
        ["ValidationErrors.PasswordStrength.VeryWeak"] = "Дуже слабкий",
        ["ValidationErrors.PasswordStrength.Weak"] = "Слабкий",
        ["ValidationErrors.PasswordStrength.Good"] = "Хороший",
        ["ValidationErrors.PasswordStrength.Strong"] = "Сильний",
        ["ValidationErrors.PasswordStrength.VeryStrong"] = "Дуже сильний",

        ["ValidationWarnings.SecureKey.NonLatinLetter"] = "Лише латинські літери",
        ["ValidationWarnings.SecureKey.InvalidCharacter"] = "Використано недопустимий символ",
        ["ValidationWarnings.SecureKey.MultipleCharacters"] = "Кілька символів одного типу",


        ["Welcome.SignInButton"] = "Увійти",
        ["Welcome.CreateAccountButton"] = "Створити акаунт",


        ["Footer.PrivacyPolicy"] = "Політика конфіденційності",
        ["Footer.TermsOfService"] = "Умови надання послуг",
        ["Footer.Support"] = "Підтримка",
        ["Footer.AgreementText"] = "Продовжуючи, ви погоджуєтесь з нашими Умовами та Політикою конфіденціальності.",
        ["Footer.Copyright"] = "© 2025 Horizon Dynamics. Усі права захищено.",


        ["Navigation.Back"] = "Назад",
        ["Navigation.Close"] = "Закрити",
        ["Navigation.Minimize"] = "Згорнути",
        ["Navigation.Maximize"] = "Розгорнути",


        ["Common.Loading"] = "Завантаження...",
        ["Common.Error"] = "Помилка",
        ["Common.Success"] = "Успішно",
        ["Common.Cancel"] = "Скасувати",
        ["Common.UnexpectedError"] = "Несподівана помилка",
        ["Common.Ok"] = "Гаразд",
        ["Common.NoInternetConnection"] = "Немає підключення до інтернету",
        ["Common.CheckConnection"] = "Перевірте ваше інтернет-з'єднання",


        ["NetworkNotification.NoInternet.Title"] = "Немає підключення до інтернету",
        ["NetworkNotification.NoInternet.Description"] = "Перевірте з'єднання та спробуйте знову",
        ["NetworkNotification.CheckingInternet.Title"] = "Перевірка з'єднання",
        ["NetworkNotification.CheckingInternet.Description"] = "Перевіряємо підключення до інтернету",
        ["NetworkNotification.InternetRestored.Title"] = "З'єднання відновлено",
        ["NetworkNotification.InternetRestored.Description"] = "Підключення до інтернету відновлено",
        ["NetworkNotification.Connecting.Title"] = "Підключення",
        ["NetworkNotification.Connecting.Description"] = "Встановлюємо з'єднання з сервером",
        ["NetworkNotification.Reconnecting.Title"] = "Перепідключення",
        ["NetworkNotification.Reconnecting.Description"] = "Намагаємось відновити з'єднання з сервером",
        ["NetworkNotification.ServerNotResponding.Title"] = "Сервер недоступний",
        ["NetworkNotification.ServerNotResponding.Description"] = "Наші сервери не відповідають",
        ["NetworkNotification.ServerShuttingDown.Title"] = "Обслуговування сервера",
        ["NetworkNotification.ServerShuttingDown.Description"] = "Сервер вимикається",
        ["NetworkNotification.RetriesExhausted.Title"] = "Не вдалось підключитись",
        ["NetworkNotification.RetriesExhausted.Description"] = "Неможливо підключитись після кількох спроб",
        ["NetworkNotification.ServerReconnected.Title"] = "Підключено",
        ["NetworkNotification.ServerReconnected.Description"] = "Успішно підключено до сервера",
        ["NetworkNotification.Button.Retry"] = "Повторити",


        ["LanguageDetection.Title"] = "Пропозиція мови",
        ["LanguageDetection.Prompt"] = "Перемкнутись на {0}?",
        ["LanguageDetection.Button.Confirm"] = "Змінити мову",
        ["LanguageDetection.Button.Decline"] = "Не зараз"
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, FrozenDictionary<string, string>> AllLanguages = new Dictionary<string, FrozenDictionary<string, string>>
    {
        ["en-US"] = EnglishStrings,
        ["uk-UA"] = UkrainianStrings
    }.ToFrozenDictionary();
}
