namespace Ecliptix.Core.Services.Membership.Constants;

public static class MobileNumberValidatorConstants
{
    public static class LocalizationKeys
    {
        public const string CANNOT_BE_EMPTY = "ValidationErrors.MobileNumber.CANNOT_BE_EMPTY";
        public const string MUST_START_WITH_COUNTRY_CODE = "ValidationErrors.MobileNumber.MUST_START_WITH_COUNTRY_CODE";
        public const string CONTAINS_NON_DIGITS = "ValidationErrors.MobileNumber.CONTAINS_NON_DIGITS";
        public const string INCORRECT_LENGTH = "ValidationErrors.MobileNumber.INCORRECT_LENGTH";
    }

    public static class ValidationRules
    {
        public const string COUNTRY_CODE_PREFIX = "+";
        public const int MIN_DIGITS = 7;
        public const int MAX_DIGITS = 15;
    }
}
