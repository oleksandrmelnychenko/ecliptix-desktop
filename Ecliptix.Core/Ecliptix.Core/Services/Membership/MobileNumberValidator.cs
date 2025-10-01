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
        List<(Func<string, bool> IsInvalid, string ErrorMessageKey)> validationRules =
        [
            (string.IsNullOrWhiteSpace, MobileNumberValidatorConstants.LocalizationKeys.CannotBeEmpty),
            (s => !s.StartsWith(MobileNumberValidatorConstants.ValidationRules.CountryCodePrefix),
                MobileNumberValidatorConstants.LocalizationKeys.MustStartWithCountryCode),
            (s => s.Length > 1 && ContainsNonDigitsRegex().IsMatch(s[1..]),
                MobileNumberValidatorConstants.LocalizationKeys.ContainsNonDigits),
            (s => s.Length is < MobileNumberValidatorConstants.ValidationRules.MinDigits + 1
                or > MobileNumberValidatorConstants.ValidationRules.MaxDigits + 1,
                MobileNumberValidatorConstants.LocalizationKeys.IncorrectLength)
        ];

        foreach ((Func<string, bool> isInvalid, string errorMessageKey) in validationRules)
        {
            if (isInvalid(mobileNumber))
            {
                if (errorMessageKey == MobileNumberValidatorConstants.LocalizationKeys.IncorrectLength)
                {
                    string formatString = localizationService[errorMessageKey];
                    return string.Format(formatString,
                        MobileNumberValidatorConstants.ValidationRules.MinDigits,
                        MobileNumberValidatorConstants.ValidationRules.MaxDigits);
                }

                return localizationService[errorMessageKey];
            }
        }

        return string.Empty;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex ContainsNonDigitsRegex();
}