using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Core.Localization;

namespace Ecliptix.Core.Services.Membership;

public static partial class NameValidator
{
    private const int MinProfileNameLength = 3;
    private const int MaxProfileNameLength = 30;
    private const int MaxDisplayNameLength = 50;

    //TODO decide the place where it should be moved
    private static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "support", "root", "system", "ecliptix", "null", "undefined"
    };

    public static string ValidateProfileName(string? name, ILocalizationService localizationService)
    {
        string input = name ?? string.Empty;

        List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> validationRules =
        [
            (string.IsNullOrWhiteSpace,
             LocalizationKeys.ValidationErrors.Profile.REQUIRED, null),

            (s => s.Length < MinProfileNameLength || s.Length > MaxProfileNameLength,
             LocalizationKeys.ValidationErrors.Profile.INVALID_LENGTH,
             [MinProfileNameLength, MaxProfileNameLength]),

            (s => !IsAllowedProfileRegex().IsMatch(s),
             LocalizationKeys.ValidationErrors.Profile.INVALID_CHARACTERS, null),

            (s => s.StartsWith('.') || s.StartsWith('_') || s.StartsWith('-') ||
                  s.EndsWith('.') || s.EndsWith('_') || s.EndsWith('-'),
             LocalizationKeys.ValidationErrors.Profile.INVALID_START_END, null),

            (s => s.Contains("..") || s.Contains("__") || s.Contains("--") || s.Contains(".-") || s.Contains("_."),
             LocalizationKeys.ValidationErrors.Profile.CONSECUTIVE_SEPARATORS, null),

            (s => ReservedWords.Contains(s),
             LocalizationKeys.ValidationErrors.Profile.RESERVED_WORD, null)
        ];

        return ProcessValidation(input, validationRules, localizationService);
    }

    public static string ValidateDisplayName(string? name, ILocalizationService localizationService)
    {
        string input = name?.Trim() ?? string.Empty;

        List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> validationRules =
        [
            (string.IsNullOrWhiteSpace,
             LocalizationKeys.ValidationErrors.Profile.REQUIRED_DISPLAY_NAME, null),

            (s => s.Length > MaxDisplayNameLength,
             LocalizationKeys.ValidationErrors.Profile.DISPLAY_NAME_TOO_LONG,
             [MaxDisplayNameLength]),

            (s => !IsAllowedDisplayRegex().IsMatch(s),
             LocalizationKeys.ValidationErrors.Profile.INVALID_DISPLAY_CHARS, null),

            (s => s.Contains("  "),
             LocalizationKeys.ValidationErrors.Profile.NO_DOUBLE_SPACES, null)
        ];

        return ProcessValidation(input, validationRules, localizationService);
    }

    private static string ProcessValidation(
        string input,
        List<(Func<string, bool> IsInvalid, string ErrorMessageKey, object[]? Args)> rules,
        ILocalizationService localizationService)
    {
        foreach ((Func<string, bool> isInvalid, string errorMessageKey, object[]? args) in rules)
        {
            if (isInvalid(input))
            {
                string message = localizationService[errorMessageKey];
                return args != null && args.Length > 0 ? string.Format(message, args) : message;
            }
        }

        return string.Empty;
    }

    [GeneratedRegex(@"^[a-zA-Z0-9._-]+$")]
    private static partial Regex IsAllowedProfileRegex();

    [GeneratedRegex(@"^[\p{L}\p{N}\s\-\']+$")]
    private static partial Regex IsAllowedDisplayRegex();
}
