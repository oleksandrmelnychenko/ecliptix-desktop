using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Avalonia.Threading;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Core.Localization;

internal sealed class LocalizationService : ILocalizationService
{
    private readonly FrozenDictionary<string, string> _defaultLanguageStrings;
    private readonly Lock _cultureChangeLock = new();

    private FrozenDictionary<string, string> _currentLanguageStrings;
    private CultureInfo _currentCultureInfo;

    public event Action? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged = delegate { };

    public CultureInfo CurrentCultureInfo => _currentCultureInfo;
    public string CurrentCultureName => _currentCultureInfo.Name;

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return "[INVALID_KEY]";
            }

            if (_currentLanguageStrings.TryGetValue(key, out string? value))
            {
                return value;
            }

            return _defaultLanguageStrings.TryGetValue(key, out string? defaultValue) ? defaultValue : $"!{key}!";
        }
    }

    public LocalizationService(DefaultSystemSettings defaultSystemSettings)
    {
        string? defaultCultureName = defaultSystemSettings.Culture;

        _currentCultureInfo = CreateCultureInfo(defaultCultureName);
        _defaultLanguageStrings = LocalizationData.EnglishStrings;

        _currentLanguageStrings = GetLanguageStrings(defaultCultureName.ToOption())
            .ValueOr(_defaultLanguageStrings);
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
        catch (FormatException)
        {
            return formatString;
        }
    }

    public void SetCulture(string? cultureName, Action? onCultureChanged = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cultureName, nameof(cultureName));

        CultureInfo newCultureInfo = CreateCultureInfo(cultureName);

        lock (_cultureChangeLock)
        {
            if (_currentCultureInfo.Name.Equals(newCultureInfo.Name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentCultureInfo = newCultureInfo;
            _currentLanguageStrings = GetLanguageStrings(Option<string>.Some(cultureName))
                .ValueOr(_defaultLanguageStrings);
        }

        if (onCultureChanged is not null)
        {
            OnLanguageChanged();
            NotifyAllPropertiesChanged();
            onCultureChanged.Invoke();
        }
    }

    private static CultureInfo CreateCultureInfo(string? cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName ?? "en-US");
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.GetCultureInfo("en-US");
        }
    }

    private static Option<FrozenDictionary<string, string>> GetLanguageStrings(Option<string> cultureName)
    {
        return cultureName
            .Where(name => !string.IsNullOrEmpty(name))
            .Bind(name => LocalizationData.AllLanguages.GetValueOrDefault(name).ToOption());
    }

    private void OnLanguageChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            LanguageChanged?.Invoke();
        }
        else
        {
            Dispatcher.UIThread.Post(() => LanguageChanged?.Invoke());
        }
    }

    private void NotifyAllPropertiesChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            });
        }
    }
}
