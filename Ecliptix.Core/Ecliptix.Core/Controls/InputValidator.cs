/*using System.Text.RegularExpressions;

namespace Ecliptix.Core.Controls;

public static partial class InputValidator
{
    private static readonly Regex PhonePattern = new(@"^\+\d{7,15}$", RegexOptions.Compiled);

    private static readonly Regex SecureKeyPattern = SecureKeyRegex();

    private static readonly Regex OnlyDigitsAfterPlusPattern = new(@"^\+\d+$", RegexOptions.Compiled);
    private static readonly Regex PrintableAsciiPattern = new(@"^[\x20-\x7E]+$", RegexOptions.Compiled);
    private static readonly Regex UppercasePattern = new(@"[A-Z]", RegexOptions.Compiled);
    private static readonly Regex SpecialCharPattern = new(@"[!@#$%^&*(),.?""{}|<>/]", RegexOptions.Compiled);

    public static string? Validate(string input, ValidationType type)
    {
        return type switch
        {
            ValidationType.PhoneNumber => ValidatePhoneNumber(input),
            ValidationType.SecureKey => ValidatePassword(input),

            _ => null
        };
    }

    private static string? ValidatePhoneNumber(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return "Empty";

        if (!input.StartsWith("+"))
            return "Start with +";

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
            return "Need special char";#1#

        return null;
    }

    [GeneratedRegex(@"^(?=.*[A-Z])(?=.*[!@#$%^&*(),.?""{}|<>/])[\x20-\x7E]{8,}$", RegexOptions.Compiled)]
    private static partial Regex SecureKeyRegex();
}*/