using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Settings;

public sealed class AppCultureSettings
{
    private static readonly Lazy<AppCultureSettings> Instance = new(() => new AppCultureSettings());

    public static AppCultureSettings Default => Instance.Value;

    private readonly FrozenDictionary<string, LanguageItem> _languagesByCode;
    private readonly FrozenDictionary<string, string> _countryCultureMap;
    private readonly FrozenDictionary<string, int> _languageIndexMap;

    private AppCultureSettings()
    {
        List<LanguageItem> supportedLanguages = new(AppCultureSettingsConstants.INITIAL_CAPACITY)
        {
            new LanguageItem(AppCultureSettingsConstants.DEFAULT_CULTURE_CODE,
                AppCultureSettingsConstants.UNITED_STATES_COUNTRY_CODE,
                AppCultureSettingsConstants.UNITED_STATES_FLAG_PATH),
            new LanguageItem(AppCultureSettingsConstants.UKRAINIAN_CULTURE_CODE,
                AppCultureSettingsConstants.UKRAINE_COUNTRY_CODE,
                AppCultureSettingsConstants.UKRAINE_FLAG_PATH)
        };

        Dictionary<string, string> countryCultureMap = new(AppCultureSettingsConstants.INITIAL_CAPACITY)
        {
            { AppCultureSettingsConstants.UNITED_STATES_COUNTRY_CODE, AppCultureSettingsConstants.DEFAULT_CULTURE_CODE },
            { AppCultureSettingsConstants.UKRAINE_COUNTRY_CODE, AppCultureSettingsConstants.UKRAINIAN_CULTURE_CODE },
        };

        _languagesByCode = supportedLanguages.ToFrozenDictionary(lang => lang.Code, lang => lang);
        _countryCultureMap = countryCultureMap.ToFrozenDictionary();
        _languageIndexMap = CreateLanguageIndexMap(supportedLanguages);
    }

    private static FrozenDictionary<string, int> CreateLanguageIndexMap(List<LanguageItem> supportedLanguages)
    {
        Dictionary<string, int> indexMap = new(supportedLanguages.Count);
        for (int i = 0; i < supportedLanguages.Count; i++)
        {
            indexMap[supportedLanguages[i].Code] = i;
        }
        return indexMap.ToFrozenDictionary();
    }

    public IReadOnlyCollection<LanguageItem> SupportedLanguages => _languagesByCode.Values;

    public Option<LanguageItem> GetLanguageByCode(string cultureCode) =>
        _languagesByCode.GetValueOrDefault(cultureCode).ToOption();

    public string GetCultureByCountry(string countryCode) =>
        _countryCultureMap.GetValueOrDefault(countryCode?.ToUpperInvariant() ?? AppCultureSettingsConstants.EmptyString, AppCultureSettingsConstants.DEFAULT_CULTURE_CODE);

    public int GetLanguageIndex(string cultureCode) =>
        _languageIndexMap.GetValueOrDefault(cultureCode, AppCultureSettingsConstants.DEFAULT_LANGUAGE_INDEX);

    public string GetDisplayName(string cultureCode)
    {
        return GetLanguageByCode(cultureCode)
            .Select(lang => lang.DisplayName)
            .GetValueOrDefault(() =>
            {
                try
                {
                    CultureInfo culture = CultureInfo.GetCultureInfo(cultureCode);
                    string englishName = culture.EnglishName;
                    int separatorIndex = englishName.IndexOf(AppCultureSettingsConstants.CULTURE_DISPLAY_NAME_SEPARATOR);
                    return separatorIndex > 0 ? englishName[..separatorIndex].Trim() : englishName.Trim();
                }
                catch (CultureNotFoundException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[APP-CULTURE] CULTURE not found: {cultureCode}, ERROR: {ex.Message}");
                    return cultureCode;
                }
            });
    }
}
