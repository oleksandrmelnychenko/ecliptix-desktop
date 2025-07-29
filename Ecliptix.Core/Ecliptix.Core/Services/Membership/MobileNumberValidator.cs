using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Ecliptix.Core.Services.Membership;

public static partial class MobileNumberValidator
{
    private const int MinDigits = 7;
    private const int MaxDigits = 15;

    public static string Validate(string phoneNumber, ILocalizationService localizationService)
    {
        List<(Func<string, bool> IsInvalid, string ErrorMessageKey)> validationRules =
        [
            (string.IsNullOrWhiteSpace, "ValidationErrors.PhoneNumber.CannotBeEmpty"),
            (s => !s.StartsWith("+"), "ValidationErrors.PhoneNumber.MustStartWithCountryCode"),
            (s => s.Length > 1 && ContainsNonDigitsRegex().IsMatch(s[1..]),
                "ValidationErrors.PhoneNumber.ContainsNonDigits"),
            (s => s.Length is < MinDigits + 1 or > MaxDigits + 1, "ValidationErrors.PhoneNumber.IncorrectLength")
        ];

        foreach ((Func<string, bool> isInvalid, string errorMessageKey) in validationRules)
        {
            if (isInvalid(phoneNumber))
            {
                if (errorMessageKey == "ValidationErrors.PhoneNumber.IncorrectLength")
                {
                    string formatString = localizationService[errorMessageKey];
                    return string.Format(formatString, MinDigits, MaxDigits);
                }
                
                return localizationService[errorMessageKey];
            }
        }

        return string.Empty;
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex ContainsNonDigitsRegex();
}