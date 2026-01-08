using System;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Services.Core.Localization;

internal static class LocalizationServiceExtensions
{
    private const string InvalidKeyPlaceholder = "[INVALID_KEY]";

    public static string ResolveMessageKey(
        this ILocalizationService localizationService,
        string? messageKey,
        string fallbackKey)
    {
        if (string.IsNullOrWhiteSpace(messageKey))
        {
            return localizationService[fallbackKey];
        }

        string localized = localizationService[messageKey];

        if (string.Equals(localized, InvalidKeyPlaceholder, StringComparison.Ordinal)
            || (localized.Length > 1 && localized[0] == '!' && localized[^1] == '!'))
        {
            return localizationService[fallbackKey];
        }

        return localized;
    }
}
