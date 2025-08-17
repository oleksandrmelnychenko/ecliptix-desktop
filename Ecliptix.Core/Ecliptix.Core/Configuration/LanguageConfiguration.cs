using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ecliptix.Core.Controls.LanguageSelector;

namespace Ecliptix.Core.Configuration;

public static class LanguageConfiguration
{
    public static readonly FrozenDictionary<string, string> SupportedCountries =
        new Dictionary<string, string>
        {
            { "UA", "uk-UA" },
            { "US", "en-US" },
        }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, LanguageItem> LanguageCodeMap =
        new Dictionary<string, LanguageItem>
        {
            ["en-US"] = new("en-US", "EN", "avares://Ecliptix.Core/Assets/Flags/usa_flag.svg"),
            ["uk-UA"] = new("uk-UA", "UK", "avares://Ecliptix.Core/Assets/Flags/ukraine_flag.svg")
        }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, string> FlagMap =
        LanguageCodeMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FlagImagePath).ToFrozenDictionary();

    public static readonly FrozenDictionary<string, int> LanguageIndexMap =
        new Dictionary<string, int>
        {
            ["en-US"] = 0,
            ["uk-UA"] = 1
        }.ToFrozenDictionary();

    public const string DefaultCulture = "en-US";

    public static string GetCultureByCountry(string country) =>
        SupportedCountries.GetValueOrDefault(country, DefaultCulture);

    public static LanguageItem? GetLanguageByCode(string cultureCode) =>
        LanguageCodeMap.TryGetValue(cultureCode, out LanguageItem? item) ? item : null;

    public static int GetLanguageIndex(string cultureCode) =>
        LanguageIndexMap.TryGetValue(cultureCode, out int index) ? index : 0;

    public static string GetDisplayName(string cultureCode)
    {
        var culture = CultureInfo.GetCultureInfo(cultureCode);
        return culture.EnglishName.Split('(')[0].Trim();
    }
}