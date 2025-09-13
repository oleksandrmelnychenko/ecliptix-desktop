using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Settings.Constants;

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
        List<LanguageItem> supportedLanguages = new(AppCultureSettingsConstants.InitialCapacity)
        {
            new LanguageItem(AppCultureSettingsConstants.DefaultCultureCode,
                AppCultureSettingsConstants.UnitedStatesCountryCode,
                AppCultureSettingsConstants.UnitedStatesFlagPath),
            new LanguageItem(AppCultureSettingsConstants.UkrainianCultureCode,
                AppCultureSettingsConstants.UkraineCountryCode,
                AppCultureSettingsConstants.UkraineFlagPath)
        };

        Dictionary<string, string> countryCultureMap = new(AppCultureSettingsConstants.InitialCapacity)
        {
            { AppCultureSettingsConstants.UnitedStatesCountryCode, AppCultureSettingsConstants.DefaultCultureCode },
            { AppCultureSettingsConstants.UkraineCountryCode, AppCultureSettingsConstants.UkrainianCultureCode },
        };

        _languagesByCode = supportedLanguages.ToFrozenDictionary(lang => lang.Code, lang => lang);
        _countryCultureMap = countryCultureMap.ToFrozenDictionary();
        _languageIndexMap = CreateLanguageIndexMap(supportedLanguages);
    }

    private static FrozenDictionary<string, int> CreateLanguageIndexMap(List<LanguageItem> supportedLanguages)
    {
        Dictionary<string, int> indexMap = new Dictionary<string, int>(supportedLanguages.Count);
        for (int i = 0; i < supportedLanguages.Count; i++)
        {
            indexMap[supportedLanguages[i].Code] = i;
        }
        return indexMap.ToFrozenDictionary();
    }

    public IReadOnlyCollection<LanguageItem> SupportedLanguages => _languagesByCode.Values;

    public LanguageItem? GetLanguageByCode(string cultureCode) =>
        _languagesByCode.GetValueOrDefault(cultureCode);

    public string GetCultureByCountry(string countryCode) =>
        _countryCultureMap.GetValueOrDefault(countryCode?.ToUpperInvariant() ?? AppCultureSettingsConstants.EmptyString, AppCultureSettingsConstants.DefaultCultureCode);

    public int GetLanguageIndex(string cultureCode) =>
        _languageIndexMap.GetValueOrDefault(cultureCode, AppCultureSettingsConstants.DefaultLanguageIndex);

    public string GetDisplayName(string cultureCode)
    {
        LanguageItem? languageItem = GetLanguageByCode(cultureCode);
        if (languageItem != null)
            return languageItem.DisplayName;

        try
        {
            CultureInfo culture = CultureInfo.GetCultureInfo(cultureCode);
            string englishName = culture.EnglishName;
            int separatorIndex = englishName.IndexOf(AppCultureSettingsConstants.CultureDisplayNameSeparator);
            return separatorIndex > 0 ? englishName[..separatorIndex].Trim() : englishName.Trim();
        }
        catch (CultureNotFoundException)
        {
            return cultureCode;
        }
    }
}