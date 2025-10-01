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
            (string.IsNullOrWhiteSpace, MobileNumberValidatorConstants.LocalizationKeys.CannotBeEmpty, null),
            (s => !s.StartsWith(MobileNumberValidatorConstants.ValidationRules.CountryCodePrefix),
                MobileNumberValidatorConstants.LocalizationKeys.MustStartWithCountryCode, null),
            (s => s.Length > 1 && ContainsNonDigitsRegex().IsMatch(s[1..]),
                MobileNumberValidatorConstants.LocalizationKeys.ContainsNonDigits, null),
            (s => s.Length is < MobileNumberValidatorConstants.ValidationRules.MinDigits + 1
                or > MobileNumberValidatorConstants.ValidationRules.MaxDigits + 1,
                MobileNumberValidatorConstants.LocalizationKeys.IncorrectLength,
                [MobileNumberValidatorConstants.ValidationRules.MinDigits,
                 MobileNumberValidatorConstants.ValidationRules.MaxDigits])
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