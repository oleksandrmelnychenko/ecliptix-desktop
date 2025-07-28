using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Ecliptix.Core.Services.Membership;

public static partial class SecureKeyValidator
{
    private const int MinLength = 8;
    private const int MaxLength = 21;
    private const int MinCharClasses = 3;
    private const double MinTotalEntropyBits = 50;

    private static readonly Regex HasLowercaseRegex = HasLowercaseRegexPattern();
    private static readonly Regex HasUppercaseRegex = HasUppercaseRegexPattern();
    private static readonly Regex HasDigitRegex = HasDigitRegexPattern();
    private static readonly Regex HasSpecialCharRegex = HasSpecialCharRegexPattern();

    [GeneratedRegex("[a-z]", RegexOptions.Compiled)]
    private static partial Regex HasLowercaseRegexPattern();

    [GeneratedRegex("[A-Z]", RegexOptions.Compiled)]
    private static partial Regex HasUppercaseRegexPattern();

    [GeneratedRegex(@"\d", RegexOptions.Compiled)]
    private static partial Regex HasDigitRegexPattern();

    [GeneratedRegex(@"[^a-zA-Z\d]", RegexOptions.Compiled)]
    private static partial Regex HasSpecialCharRegexPattern();

    public static string? Validate(string secureKey, ILocalizationService localizationService)
    {
        List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> validationRules =
        [
            (string.IsNullOrWhiteSpace, "ValidationErrors.SecureKey.Required", null),
            (s => s.Length < MinLength, "ValidationErrors.SecureKey.MinLength", [MinLength]),
            (s => s.Length > MaxLength, "ValidationErrors.SecureKey.MaxLength", [MaxLength]),
            (s => s.Trim() != s, "ValidationErrors.SecureKey.NoSpaces", null),
            (s => CalculateTotalShannonEntropy(s) < MinTotalEntropyBits, "ValidationErrors.SecureKey.TooSimple", null),
            (s => CommonlyUsedPasswords.Contains(s), "ValidationErrors.SecureKey.TooCommon", null),
            (IsSequentialOrKeyboardPattern, "ValidationErrors.SecureKey.SequentialPattern", null),
            (HasExcessiveRepeats, "ValidationErrors.SecureKey.RepeatedChars", null),
            (LacksCharacterDiversity, "ValidationErrors.SecureKey.LacksDiversity", [MinCharClasses]),
            (ContainsAppNameVariant, "ValidationErrors.SecureKey.ContainsAppName", null)
        ];

        foreach ((Func<string, bool> isInvalid, string errorMessageKey, object[]? args) in validationRules)
        {
            if (isInvalid(secureKey))
            {
                string message = localizationService[errorMessageKey];
                return args != null ? string.Format(message, args) : message;
            }
        }

        return null;
    }

    public static PasswordStrength EstimatePasswordStrength(string secureKey, ILocalizationService localizationService)
    {
        if (Validate(secureKey, localizationService) is not null)
        {
            return string.IsNullOrWhiteSpace(secureKey) ? PasswordStrength.Invalid : PasswordStrength.VeryWeak;
        }

        int score = 0;
        score += secureKey.Length switch
        {
            >= 16 => 4, >= 12 => 3, >= 10 => 2, >= 8 => 1, _ => 0
        };

        int variety = GetCharacterClassCount(secureKey);
        if (variety >= 3) score += 2;
        if (variety == 4) score += 1;

        return score switch
        {
            <= 2 => PasswordStrength.Weak,
            <= 4 => PasswordStrength.Good,
            <= 6 => PasswordStrength.Strong,
            _ => PasswordStrength.VeryStrong,
        };
    }

    private static readonly HashSet<string> CommonlyUsedPasswords =
        new(GetTopCommonPasswords(), StringComparer.OrdinalIgnoreCase);

    private static readonly List<string> KeyboardRows = ["qwertyuiop", "asdfghjkl", "zxcvbnm", "1234567890"];
    private static readonly List<string> AppNameVariants = ["ecliptix", "eclip", "opaque"];

    private static IEnumerable<string> GetTopCommonPasswords()
    {
        yield return "123456";
        yield return "password";
        yield return "12345678";
        yield return "123456789";
        yield return "qwerty";
    }

    private static bool IsSequentialOrKeyboardPattern(string s)
    {
        if (s.Length < 4) return false;
        string lower = s.ToLowerInvariant();
        for (int i = 0; i <= lower.Length - 4; i++)
        {
            string sub = lower.Substring(i, 4);
            if (KeyboardRows.Any(row => row.Contains(sub)) || IsCharSequence(sub)) return true;
        }

        return false;
    }

    private static bool IsCharSequence(string sub)
    {
        bool asc = true, desc = true;
        for (int j = 1; j < sub.Length; j++)
        {
            if (sub[j] != sub[j - 1] + 1) asc = false;
            if (sub[j] != sub[j - 1] - 1) desc = false;
        }

        return asc || desc;
    }

    private static bool HasExcessiveRepeats(string s)
    {
        if (s.Length < 4) return false;
        for (int i = 0; i <= s.Length - 4; i++)
        {
            if (s[i] == s[i + 1] && s[i] == s[i + 2] && s[i] == s[i + 3]) return true;
        }

        return false;
    }

    private static bool LacksCharacterDiversity(string s) => GetCharacterClassCount(s) < MinCharClasses;

    private static int GetCharacterClassCount(string s)
    {
        int classes = 0;
        if (HasLowercaseRegex.IsMatch(s)) classes++;
        if (HasUppercaseRegex.IsMatch(s)) classes++;
        if (HasDigitRegex.IsMatch(s)) classes++;
        if (HasSpecialCharRegex.IsMatch(s)) classes++;
        return classes;
    }

    private static bool ContainsAppNameVariant(string s) =>
        AppNameVariants.Any(v => s.Contains(v, StringComparison.InvariantCultureIgnoreCase));

    private static double CalculateTotalShannonEntropy(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        Dictionary<char, int> freqMap = s.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        double totalLength = s.Length;
        double perCharEntropy = freqMap.Values
            .Select(count => count / totalLength)
            .Sum(p => -p * Math.Log(p, 2));
        return perCharEntropy * totalLength;
    }
}