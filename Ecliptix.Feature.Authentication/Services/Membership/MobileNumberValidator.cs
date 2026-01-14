using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Authentication.Services.Membership.Constants;
using Ecliptix.Utilities;

namespace Ecliptix.Feature.Authentication.Services.Membership;

public static partial class MobileNumberValidator
{
    public static string Validate(string mobileNumber, ILocalizationService localizationService)
    {
        List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> validationRules =
        [
            (string.IsNullOrWhiteSpace,
                MobileNumberValidatorConstants.LocalizationKeys.CANNOT_BE_EMPTY, null),
            (s => !IsAllowedCharactersRegex().IsMatch(s),
                MobileNumberValidatorConstants.LocalizationKeys.CONTAINS_NON_DIGITS, null),
            (s => MobileNumberHelper.Normalize(s).Length < MobileNumberValidatorConstants.ValidationRules.MIN_DIGITS
                  || MobileNumberHelper.Normalize(s).Length > MobileNumberValidatorConstants.ValidationRules.MAX_DIGITS,
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

    [GeneratedRegex(@"^[0-9\s-]+$")]
    private static partial Regex IsAllowedCharactersRegex();
}
