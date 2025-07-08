using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Ecliptix.Core.Settings;

namespace Ecliptix.Core.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly IReadOnlyDictionary<string, string> _localizedStrings;
    private readonly CultureInfo _currentCultureInfo;
    private readonly IReadOnlyDictionary<string, string> _defaultCultureStrings;

    private readonly Lock _cultureChangeLock = new();

    public event Action? LanguageChanged;

    public CultureInfo CurrentCultureInfo => _currentCultureInfo;
    public string CurrentCultureName => _currentCultureInfo.Name;

    public LocalizationService(DefaultAppSettings defaultAppSettings)
    {
        string defaultCultureName = defaultAppSettings.Culture;

        _defaultCultureStrings = new Dictionary<string, string>();

        _localizedStrings = _defaultCultureStrings;
        _currentCultureInfo = CreateCultureInfo(defaultCultureName);
    }

    private static CultureInfo CreateCultureInfo(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException ex)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "[INVALID_KEY]";
            }

            if (_localizedStrings.TryGetValue(key, out string? value))
            {
                return value;
            }

            if (_defaultCultureStrings.TryGetValue(key, out var defaultValue))
            {
                return defaultValue;
            }

            return $"!{key}!";
        }
    }

    public string GetString(string key, params object[] args)
    {
        string formatString = this[key];
        if (formatString.StartsWith("!") && formatString.EndsWith("!") || args.Length == 0)
        {
            return formatString;
        }

        try
        {
            return string.Format(_currentCultureInfo, formatString, args);
        }
        catch (FormatException ex)
        {
            return formatString;
        }
    }

    public void SetCulture(string cultureName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName, nameof(cultureName));

        CultureInfo newCultureInfo = CreateCultureInfo(cultureName);

        lock (_cultureChangeLock)
        {
            if (_currentCultureInfo.Name.Equals(newCultureInfo.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }
    }

    private void OnLanguageChanged() =>
        LanguageChanged?.Invoke();
}