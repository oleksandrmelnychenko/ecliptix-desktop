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

    private static List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> GetHardRules() =>
    [
        (string.IsNullOrWhiteSpace, SecureKeyValidatorConstants.LocalizationKeys.REQUIRED, null),
        (HasNonEnglishLetters, SecureKeyValidatorConstants.LocalizationKeys.NON_ENGLISH_LETTERS, null),
        (s => s.Any(char.IsWhiteSpace), SecureKeyValidatorConstants.LocalizationKeys.NO_SPACES, null),
        (s => s.Length < SecureKeyValidatorConstants.ValidationRules.MIN_LENGTH,
            SecureKeyValidatorConstants.LocalizationKeys.MIN_LENGTH,
            [SecureKeyValidatorConstants.ValidationRules.MIN_LENGTH]),
        (s => !HasUppercaseRegex.IsMatch(s), SecureKeyValidatorConstants.LocalizationKeys.NO_UPPERCASE, null),
        (s => !HasLowercaseRegex.IsMatch(s), SecureKeyValidatorConstants.LocalizationKeys.NO_LOWERCASE, null),
        (s => !HasSpecialCharRegex.IsMatch(s), SecureKeyValidatorConstants.LocalizationKeys.NO_SPECIAL_CHAR, null),
        (s => !HasDigitRegex.IsMatch(s), SecureKeyValidatorConstants.LocalizationKeys.NO_DIGIT, null)
    ];

    private static List<(Func<string, bool> IsWeak, string ErrorMessageKey, object[]? Args)> GetSoftRules() =>
    [
        (s => s.Length > SecureKeyValidatorConstants.ValidationRules.MAX_LENGTH,
            SecureKeyValidatorConstants.LocalizationKeys.MAX_LENGTH,
            [SecureKeyValidatorConstants.ValidationRules.MAX_LENGTH]),
        (s => SecureKeyValidatorConstants.CommonlyUsedSecureKeys.Contains(s),
            SecureKeyValidatorConstants.LocalizationKeys.TOO_COMMON, null),
        (IsSequentialOrKeyboardPattern, SecureKeyValidatorConstants.LocalizationKeys.SEQUENTIAL_PATTERN, null),
        (HasExcessiveRepeats, SecureKeyValidatorConstants.LocalizationKeys.REPEATED_CHARS, null),
        (LacksCharacterDiversity, SecureKeyValidatorConstants.LocalizationKeys.LACKS_DIVERSITY,
            [SecureKeyValidatorConstants.ValidationRules.MIN_CHAR_CLASSES]),
        (ContainsAppNameVariant, SecureKeyValidatorConstants.LocalizationKeys.CONTAINS_APP_NAME, null),
        (s => CalculateTotalShannonEntropy(s) < SecureKeyValidatorConstants.ValidationRules.MIN_TOTAL_ENTROPY_BITS,
            SecureKeyValidatorConstants.LocalizationKeys.TOO_SIMPLE, null),
    ];

    public static (string? ERROR, List<string> Recommendations) Validate(string secureKey,
        ILocalizationService localizationService)
    {
        List<string> recommendations = [];
        string s = secureKey ?? string.Empty;

        foreach ((Func<string, bool> isInvalid, string errorMessageKey, object[]? args) in GetHardRules())
        {
            if (isInvalid(s))
            {
                string message = localizationService[errorMessageKey];
                string? error = args != null ? string.Format(message, args) : message;
                return (error, recommendations);
            }
        }

        foreach ((Func<string, bool> isWeak, string errorMessageKey, object[]? args) in GetSoftRules())
        {
            if (isWeak(s))
            {
                string message = localizationService[errorMessageKey];
                recommendations.Add(args != null ? string.Format(message, args) : message);
            }
        }

        return (null, recommendations);
    }

    public static List<(string Description, bool IsMet)> GetChecklistStatus(string secureKey, ILocalizationService localizationService)
    {
        string s = secureKey ?? string.Empty;
        List<(string Description, bool IsMet)> statusList = new();

        List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> hardRules = GetHardRules().ToList();

        string[] checklistKeys = new[]
        {
            SecureKeyValidatorConstants.LocalizationKeys.MIN_LENGTH,
            SecureKeyValidatorConstants.LocalizationKeys.NO_UPPERCASE,
            SecureKeyValidatorConstants.LocalizationKeys.NO_LOWERCASE,
            SecureKeyValidatorConstants.LocalizationKeys.NO_SPECIAL_CHAR,
            SecureKeyValidatorConstants.LocalizationKeys.NO_DIGIT,
        };

        foreach (string key in checklistKeys)
        {
            (Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args) ruleDefinition = hardRules.FirstOrDefault(r => r.ErrorMessageKey == key);

            if (ruleDefinition.IsInvalid != null)
            {
                bool isMet = !ruleDefinition.IsInvalid(s);

                string description = localizationService[key];
                if (ruleDefinition.Args != null && ruleDefinition.Args.Length > 0)
                {
                    description = string.Format(description, ruleDefinition.Args);
                }

                statusList.Add((description, isMet));
            }
        }

        return statusList;
    }

    public static SecureKeyStrength EstimateSecureKeyStrength(string secureKey, ILocalizationService localizationService)
    {
        if (string.IsNullOrEmpty(secureKey))
        {
            return SecureKeyStrength.INVALID;
        }

        (string? hardError, _) = Validate(secureKey, localizationService);
        if (hardError != null)
        {
            return SecureKeyStrength.INVALID;
        }

        List<string> softTips = GetQualityRecommendations(secureKey, localizationService);
        int penalties = softTips.Count;

        int lengthScore = secureKey.Length switch
        {
            >= 14 => 3,
            >= 12 => 2,
            >= 10 => 1,
            _ => 0
        };

        int finalScore = lengthScore - penalties;

        return finalScore switch
        {
            < 0 => SecureKeyStrength.WEAK,
            0 => SecureKeyStrength.WEAK,
            1 => SecureKeyStrength.GOOD,
            2 => SecureKeyStrength.STRONG,
            _ => SecureKeyStrength.VERY_STRONG
        };
    }

    public static List<string> GetQualityRecommendations(string secureKey, ILocalizationService localizationService)
    {
        List<string> recommendations = [];
        string s = secureKey ?? string.Empty;

        if (string.IsNullOrEmpty(s))
        {
            return recommendations;
        }

        foreach ((Func<string, bool> isWeak, string key, object[]? args) in GetSoftRules())
        {
            if (isWeak(s))
            {
                string message = localizationService[key];
                string formattedMessage = args != null ? string.Format(message, args) : message;

                recommendations.Add(formattedMessage);
            }
        }

        return recommendations;
    }

    private static bool IsSequentialOrKeyboardPattern(string s)
    {
        const int patternLen = 4;

        if (s.Length < patternLen)
        {
            return false;
        }

        string lower = s.ToLowerInvariant();

        for (int i = 0; i <= lower.Length - patternLen; i++)
        {
            string sub = lower.Substring(i, patternLen);

            if (IsCharSequence(sub))
            {
                return true;
            }

            if (SecureKeyValidatorConstants.KeyboardRows.Any(row => row.Contains(sub)))
            {
                return true;
            }

            char[] charArray = sub.ToCharArray();
            Array.Reverse(charArray);
            string reversedSub = new(charArray);

            if (SecureKeyValidatorConstants.KeyboardRows.Any(row => row.Contains(reversedSub)))
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
