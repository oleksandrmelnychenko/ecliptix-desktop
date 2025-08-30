using System.Collections.Frozen;
using System.Collections.Generic;

namespace Ecliptix.Core.Services.Core.Localization;

public static class LocalizationData
{
    public static readonly FrozenDictionary<string, string> EnglishStrings = new Dictionary<string, string>
    {

        ["Authentication.SignUp.PhoneVerification.Title"] = "Create Account",
        ["Authentication.SignUp.PhoneVerification.Description"] = "Confirm your number. We'll text you a verification code.",
        ["Authentication.SignUp.PhoneVerification.Hint"] = "Include country code",
        ["Authentication.SignUp.PhoneVerification.Watermark"] = "Mobile Number",
        ["Authentication.SignUp.PhoneVerification.Button"] = "Continue",

        ["Authentication.SignUp.VerificationCodeEntry.Title"] = "Verify Your Number",
        ["Authentication.SignUp.VerificationCodeEntry.Description"] = "Enter the code sent to your phone.",
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


        ["ValidationErrors.PhoneNumber.MustStartWithCountryCode"] = "Must start with +",
        ["ValidationErrors.PhoneNumber.ContainsNonDigits"] = "Digits only after code",
        ["ValidationErrors.PhoneNumber.IncorrectLength"] = "{0}-{1} digits required",
        ["ValidationErrors.PhoneNumber.CannotBeEmpty"] = "Required",

        ["ValidationErrors.SecureKey.Required"] = "Required",
        ["ValidationErrors.SecureKey.MinLength"] = "Min {0} characters",
        ["ValidationErrors.SecureKey.MaxLength"] = "Max {0} characters",
        ["ValidationErrors.SecureKey.NoSpaces"] = "No spaces allowed",
        ["ValidationErrors.SecureKey.NoUppercase"] = "Requires an uppercase letter",
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
        ["NetworkNotification.ServerNotResponding.Title"] = "Server Unavailable",
        ["NetworkNotification.ServerNotResponding.Description"] = "Our servers are not responding",
        ["NetworkNotification.Button.Retry"] = "Retry",


        ["LanguageDetection.Title"] = "Language Suggestion",
        ["LanguageDetection.Prompt"] = "Switch to {0}?",
        ["LanguageDetection.Button.Confirm"] = "Switch Language",
        ["LanguageDetection.Button.Decline"] = "Not Now"
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, string> UkrainianStrings = new Dictionary<string, string>
    {

        ["Authentication.SignUp.PhoneVerification.Title"] = "Створити акаунт",
        ["Authentication.SignUp.PhoneVerification.Description"] = "Підтвердьте номер. Ми надішлемо код в SMS.",
        ["Authentication.SignUp.PhoneVerification.Hint"] = "Включно з кодом країни",
        ["Authentication.SignUp.PhoneVerification.Watermark"] = "Номер телефону",
        ["Authentication.SignUp.PhoneVerification.Button"] = "Продовжити",

        ["Authentication.SignUp.VerificationCodeEntry.Title"] = "Підтвердження номера",
        ["Authentication.SignUp.VerificationCodeEntry.Description"] = "Введіть код, надісланий на ваш телефон.",
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


        ["ValidationErrors.PhoneNumber.MustStartWithCountryCode"] = "Має починатись із +",
        ["ValidationErrors.PhoneNumber.ContainsNonDigits"] = "Лише цифри після коду",
        ["ValidationErrors.PhoneNumber.IncorrectLength"] = "Потрібно {0}-{1} цифр",
        ["ValidationErrors.PhoneNumber.CannotBeEmpty"] = "Обов'язкове поле",

        ["ValidationErrors.SecureKey.Required"] = "Обов'язкове поле",
        ["ValidationErrors.SecureKey.MinLength"] = "Мін. {0} символів",
        ["ValidationErrors.SecureKey.MaxLength"] = "Макс. {0} символів",
        ["ValidationErrors.SecureKey.NoSpaces"] = "Без пробілів",
        ["ValidationErrors.SecureKey.NoUppercase"] = "Потрібна велика літера",
        ["ValidationErrors.SecureKey.NoDigit"] = "Потрібна цифра",
        ["ValidationErrors.SecureKey.TooSimple"] = "Спробуйте додати більше різноманітності",
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
        ["NetworkNotification.ServerNotResponding.Title"] = "Сервер недоступний",
        ["NetworkNotification.ServerNotResponding.Description"] = "Наші сервери не відповідають",
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