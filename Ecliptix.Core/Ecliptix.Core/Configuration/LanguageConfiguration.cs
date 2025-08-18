using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ecliptix.Core.Controls.LanguageSelector;

namespace Ecliptix.Core.Configuration;

public sealed class LanguageConfiguration
{
    private static readonly Lazy<LanguageConfiguration> Instance = new(() => new LanguageConfiguration());
    
    public static LanguageConfiguration Default => Instance.Value;

    private readonly FrozenDictionary<string, LanguageItem> _languagesByCode;
    private readonly FrozenDictionary<string, string> _countryCultureMap;
    private readonly FrozenDictionary<string, int> _languageIndexMap;

    public const string DefaultCultureCode = "en-US";

    private LanguageConfiguration()
    {
        List<LanguageItem> supportedLanguages =
        [
            new("en-US", "US", "avares://Ecliptix.Core/Assets/Flags/usa_flag.svg"),
            new("uk-UA", "UA", "avares://Ecliptix.Core/Assets/Flags/ukraine_flag.svg")
        ];

        Dictionary<string, string> countryCultureMap = new()
        {
            { "US", "en-US" },
            { "UA", "uk-UA" },
        };

        _languagesByCode = supportedLanguages.ToFrozenDictionary(lang => lang.Code, lang => lang);
        _countryCultureMap = countryCultureMap.ToFrozenDictionary();
        _languageIndexMap = supportedLanguages
            .Select((lang, index) => new { lang.Code, Index = index })
            .ToFrozenDictionary(x => x.Code, x => x.Index);
    }

    public IReadOnlyCollection<LanguageItem> SupportedLanguages => _languagesByCode.Values;

    public LanguageItem? GetLanguageByCode(string cultureCode) =>
        _languagesByCode.GetValueOrDefault(cultureCode);

    public string GetCultureByCountry(string countryCode) =>
        _countryCultureMap.GetValueOrDefault(countryCode?.ToUpperInvariant() ?? string.Empty, DefaultCultureCode);

    public int GetLanguageIndex(string cultureCode) =>
        _languageIndexMap.GetValueOrDefault(cultureCode, 0);

    public string GetDisplayName(string cultureCode)
    {
        LanguageItem? languageItem = GetLanguageByCode(cultureCode);
        if (languageItem != null)
            return languageItem.DisplayName;

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureCode);
            return culture.EnglishName.Split('(')[0].Trim();
        }
        catch (CultureNotFoundException)
        {
            return cultureCode;
        }
    }
}