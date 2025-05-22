using System.Text.RegularExpressions;

namespace Ecliptix.Core.Controls;

public static class InputValidator
{
    public static string? Validate(string input, ValidationType type)
    {
        return type switch
        {
            ValidationType.PhoneNumber => ValidatePhoneNumber(input),
            ValidationType.Password => ValidatePassword(input),

            _ => null
        };
    }

    private static string? ValidatePhoneNumber(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Empty number";

        if (!input.StartsWith("+"))
            return "Must start with +";

        if (!Regex.IsMatch(input, @"^\+\d+$"))
            return "Digits only";

        if (input.Length < 8 || input.Length > 16)
            return "Length 7–15";


        return null; // Valid
    }

    private static string? ValidatePassword(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Empty password";

        if (input.Length < 8)
            return "Too short";

        /*
        if (input.Contains(' '))
            return "No spaces";

        if (!Regex.IsMatch(input, @"^[\x20-\x7E]+$"))
            return "Only English chars";

        if (!Regex.IsMatch(input, @"[A-Z]"))
            return "Need uppercase";

        if (!Regex.IsMatch(input, @"[!@#$%^&*(),.?""{}|<>/]"))
            return "Need special char";*/

        return null;
    }
}