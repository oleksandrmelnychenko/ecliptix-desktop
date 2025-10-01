namespace Ecliptix.Core.Services.Membership.Constants;

public static class MobileNumberValidatorConstants
{
    public static class LocalizationKeys
    {
        public const string CannotBeEmpty = "ValidationErrors.MobileNumber.CannotBeEmpty";
        public const string MustStartWithCountryCode = "ValidationErrors.MobileNumber.MustStartWithCountryCode";
        public const string ContainsNonDigits = "ValidationErrors.MobileNumber.ContainsNonDigits";
        public const string IncorrectLength = "ValidationErrors.MobileNumber.IncorrectLength";
    }

    public static class ValidationRules
    {
        public const string CountryCodePrefix = "+";
        public const int MinDigits = 7;
        public const int MaxDigits = 15;
    }
}
