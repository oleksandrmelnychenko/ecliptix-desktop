using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Membership.Constants;

namespace Ecliptix.Core.Services.Membership;

public static partial class MobileNumberValidator
{
    public static string Validate(string mobileNumber, ILocalizationService localizationService)
    {
        List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> validationRules =
        [
            (string.IsNullOrWhiteSpace, MobileNumberValidatorConstants.LocalizationKeys.CANNOT_BE_EMPTY, null),
            (s => !s.StartsWith(MobileNumberValidatorConstants.ValidationRules.COUNTRY_CODE_PREFIX),
                MobileNumberValidatorConstants.LocalizationKeys.MUST_START_WITH_COUNTRY_CODE, null),
            (s => s.Length > 1 && ContainsNonDigitsRegex().IsMatch(s[1..]),
                MobileNumberValidatorConstants.LocalizationKeys.CONTAINS_NON_DIGITS, null),
            (s => s.Length is < MobileNumberValidatorConstants.ValidationRules.MIN_DIGITS + 1
                or > MobileNumberValidatorConstants.ValidationRules.MAX_DIGITS + 1,
                MobileNumberValidatorConstants.LocalizationKeys.INCORRECT_LENGTH,
                [MobileNumberValidatorConstants.ValidationRules.MIN_DIGITS,
                 MobileNumberValidatorConstants.ValidationRules.MAX_DIGITS])
        ];

        foreach ((Func<string, bool> isInvalid, string errorMessageKey, object[]? args) in validationRules)
        {
            if (isInvalid(mobileNumber))
            {
                string message = localizationService[errorMessageKey];
                return args != null ? string.Format(message, args) : message;
            }
        }

        return string.Empty;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex ContainsNonDigitsRegex();
}
