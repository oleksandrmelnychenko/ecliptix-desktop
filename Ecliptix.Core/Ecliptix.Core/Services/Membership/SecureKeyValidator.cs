using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Membership.Constants;

namespace Ecliptix.Core.Services.Membership;

public static partial class SecureKeyValidator
{

    private static readonly Regex HasUppercaseRegex = HasUppercaseRegexPattern();
    private static readonly Regex HasDigitRegex = HasDigitRegexPattern();
    private static readonly Regex HasLowercaseRegex = HasLowercaseRegexPattern();
    private static readonly Regex HasSpecialCharRegex = HasSpecialCharRegexPattern();
    private static readonly Regex HasNonEnglishLetterRegex = HasNonEnglishLetterRegexPattern();

    [GeneratedRegex("[A-Z]", RegexOptions.Compiled)]
    private static partial Regex HasUppercaseRegexPattern();

    [GeneratedRegex(@"\d", RegexOptions.Compiled)]
    private static partial Regex HasDigitRegexPattern();

    [GeneratedRegex("[a-z]", RegexOptions.Compiled)]
    private static partial Regex HasLowercaseRegexPattern();

    [GeneratedRegex(@"[^a-zA-Z\d]", RegexOptions.Compiled)]
    private static partial Regex HasSpecialCharRegexPattern();

    [GeneratedRegex(@"[\p{L}-[A-Za-z]]", RegexOptions.Compiled)]
    private static partial Regex HasNonEnglishLetterRegexPattern();

    public static (string? ERROR, List<string> Recommendations) Validate(string secureKey,
        ILocalizationService localizationService, bool _ = false)
    {
        List<string> recommendations = [];
        string? error = null;

        List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> hardValidationRules =
        [
            (HasNonEnglishLetters, SecureKeyValidatorConstants.LocalizationKeys.NON_ENGLISH_LETTERS, null),
            (string.IsNullOrWhiteSpace, SecureKeyValidatorConstants.LocalizationKeys.REQUIRED, null),
            (s => s.Length < SecureKeyValidatorConstants.ValidationRules.MIN_LENGTH,
                SecureKeyValidatorConstants.LocalizationKeys.MIN_LENGTH,
                [SecureKeyValidatorConstants.ValidationRules.MIN_LENGTH]),
            (s => !HasUppercaseRegex.IsMatch(s), SecureKeyValidatorConstants.LocalizationKeys.NO_UPPERCASE, null),
            (s => !HasLowercaseRegex.IsMatch(s), SecureKeyValidatorConstants.LocalizationKeys.NO_LOWERCASE, null),
            (s => !HasSpecialCharRegex.IsMatch(s), SecureKeyValidatorConstants.LocalizationKeys.NO_SPECIAL_CHAR, null)
        ];

        List<(Func<string, bool> IsWeak, string ErrorMessageKey, object[]? Args)> recommendationRules =
        [
            (s => !HasDigitRegex.IsMatch(s), SecureKeyValidatorConstants.LocalizationKeys.NO_DIGIT, null),
            (s => s.Length > SecureKeyValidatorConstants.ValidationRules.MAX_LENGTH,
                SecureKeyValidatorConstants.LocalizationKeys.MAX_LENGTH,
                [SecureKeyValidatorConstants.ValidationRules.MAX_LENGTH]),
            (s => s.Trim() != s, SecureKeyValidatorConstants.LocalizationKeys.NO_SPACES, null),
            (s => CalculateTotalShannonEntropy(s) < SecureKeyValidatorConstants.ValidationRules.MIN_TOTAL_ENTROPY_BITS,
                SecureKeyValidatorConstants.LocalizationKeys.TOO_SIMPLE, null),
            (s => SecureKeyValidatorConstants.CommonlyUsedSecureKeys.Contains(s),
                SecureKeyValidatorConstants.LocalizationKeys.TOO_COMMON, null),
            (IsSequentialOrKeyboardPattern, SecureKeyValidatorConstants.LocalizationKeys.SEQUENTIAL_PATTERN, null),
            (HasExcessiveRepeats, SecureKeyValidatorConstants.LocalizationKeys.REPEATED_CHARS, null),
            (LacksCharacterDiversity, SecureKeyValidatorConstants.LocalizationKeys.LACKS_DIVERSITY,
                [SecureKeyValidatorConstants.ValidationRules.MIN_CHAR_CLASSES]),
            (ContainsAppNameVariant, SecureKeyValidatorConstants.LocalizationKeys.CONTAINS_APP_NAME, null)
        ];

        foreach ((Func<string, bool> isInvalid, string errorMessageKey, object[]? args) in hardValidationRules)
        {
            bool result = isInvalid(secureKey);
            if (result)
            {
                string message = localizationService[errorMessageKey];
                error = args != null ? string.Format(message, args) : message;
                return (error, recommendations);
            }
        }

        foreach ((Func<string, bool> isWeak, string errorMessageKey, object[]? args) in recommendationRules)
        {
            bool result = isWeak(secureKey);
            if (result)
            {
                string message = localizationService[errorMessageKey];
                recommendations.Add(args != null ? string.Format(message, args) : message);
            }
        }

        return (null, recommendations);
    }

    public static SecureKeyStrength EstimateSecureKeyStrength(string secureKey, ILocalizationService localizationService)
    {
        (string? error, List<string> recommendations) = Validate(secureKey, localizationService);
        if (error != null)
        {
            return SecureKeyStrength.Invalid;
        }

        int score = 0;
        score += secureKey.Length switch
        {
            >= 12 => 4,
            >= 9 => 3,
            >= 7 => 2,
            >= 6 => 1,
            _ => 0
        };

        int variety = GetCharacterClassCount(secureKey);
        if (variety >= 2)
        {
            score += 2;
        }

        if (variety >= 3)
        {
            score += 1;
        }

        if (variety == 4)
        {
            score += 1;
        }

        score -= recommendations.Count;

        SecureKeyStrength strength = score switch
        {
            <= 2 => SecureKeyStrength.Weak,
            <= 4 => SecureKeyStrength.Good,
            <= 6 => SecureKeyStrength.Strong,
            _ => SecureKeyStrength.VeryStrong
        };
        return strength;
    }


    private static bool IsSequentialOrKeyboardPattern(string s)
    {
        if (s.Length < 4)
        {
            return false;
        }

        string lower = s.ToLowerInvariant();
        for (int i = 0; i <= lower.Length - 4; i++)
        {
            string sub = lower.Substring(i, 4);
            if (SecureKeyValidatorConstants.KeyboardRows.Any(row => row.Contains(sub)) || IsCharSequence(sub))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCharSequence(string sub)
    {
        bool asc = true, desc = true;
        for (int j = 1; j < sub.Length; j++)
        {
            if (sub[j] != sub[j - 1] + 1)
            {
                asc = false;
            }

            if (sub[j] != sub[j - 1] - 1)
            {
                desc = false;
            }
        }

        return asc || desc;
    }

    private static bool HasExcessiveRepeats(string s)
    {
        if (s.Length < 4)
        {
            return false;
        }

        for (int i = 0; i <= s.Length - 4; i++)
        {
            if (s[i] == s[i + 1] && s[i] == s[i + 2] && s[i] == s[i + 3])
            {
                return true;
            }
        }

        return false;
    }

    private static bool LacksCharacterDiversity(string s) =>
        GetCharacterClassCount(s) < SecureKeyValidatorConstants.ValidationRules.MIN_CHAR_CLASSES;

    private static int GetCharacterClassCount(string s)
    {
        int classes = 0;
        if (HasLowercaseRegex.IsMatch(s))
        {
            classes++;
        }

        if (HasUppercaseRegex.IsMatch(s))
        {
            classes++;
        }

        if (HasDigitRegex.IsMatch(s))
        {
            classes++;
        }

        if (HasSpecialCharRegex.IsMatch(s))
        {
            classes++;
        }

        return classes;
    }

    private static bool ContainsAppNameVariant(string s) =>
        SecureKeyValidatorConstants.AppNameVariants.Any(v =>
            s.Contains(v, StringComparison.InvariantCultureIgnoreCase));

    private static double CalculateTotalShannonEntropy(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return 0;
        }

        Dictionary<char, int> freqMap = s.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
        double totalLength = s.Length;
        double perCharEntropy = freqMap.Values
            .Select(count => count / totalLength)
            .Sum(p => -p * Math.Log(p, 2));
        return perCharEntropy * totalLength;
    }

    private static bool HasNonEnglishLetters(string s) => HasNonEnglishLetterRegex.IsMatch(s);
}
