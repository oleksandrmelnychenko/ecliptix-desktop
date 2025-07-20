using System.Text.RegularExpressions;

namespace Ecliptix.Utilities.Membership;

public static partial class PhoneNumberValidator
{
    private const int MinDigits = 7;
    private const int MaxDigits = 15;

    public static string? Validate(string phoneNumber)
    {
        return ValidationRules
            .Select(rule => rule.IsInvalid(phoneNumber) ? rule.ErrorMessage : null)
            .FirstOrDefault(errorMessage => errorMessage is not null);
    }

    private static readonly List<(Func<string, bool> IsInvalid, string ErrorMessage)> ValidationRules =
    [
        (string.IsNullOrWhiteSpace, "Phone number cannot be empty."),
        (s => !s.StartsWith("+"), "Must start with a country code (+)."),
        (s => ContainsNonDigitsRegex().IsMatch(s[1..]), "Can only contain digits after the country code."),
        (s => s.Length is < MinDigits + 1 or > MaxDigits + 1,
            $"Must be between {MinDigits} and {MaxDigits} digits long.")
    ];

    [GeneratedRegex(@"\D")]
    private static partial Regex ContainsNonDigitsRegex();
}