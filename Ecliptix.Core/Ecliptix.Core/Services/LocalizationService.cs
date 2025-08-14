using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Threading;
using Avalonia.Threading;
using Ecliptix.Core.Settings;

namespace Ecliptix.Core.Services;

public sealed class LocalizationService : ILocalizationService
{
    private FrozenDictionary<string, string> _currentLanguageStrings;
    private CultureInfo _currentCultureInfo;
    private readonly FrozenDictionary<string, string> _defaultLanguageStrings;

    private readonly Lock _cultureChangeLock = new();

    public event Action? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged = delegate { };

    public CultureInfo CurrentCultureInfo => _currentCultureInfo;
    public string CurrentCultureName => _currentCultureInfo.Name;

    public LocalizationService(DefaultSystemSettings defaultSystemSettings)
    {
        string? defaultCultureName = defaultSystemSettings.Culture;
        
        _currentCultureInfo = CreateCultureInfo(defaultCultureName);
        _defaultLanguageStrings = LocalizationData.EnglishStrings;
        
        _currentLanguageStrings = GetLanguageStrings(defaultCultureName) ?? _defaultLanguageStrings;
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
    
    private static FrozenDictionary<string, string>? GetLanguageStrings(string? cultureName)
    {
        if (string.IsNullOrEmpty(cultureName))
            return null;
            
        return LocalizationData.AllLanguages.TryGetValue(cultureName, out var strings) ? strings : null;
    }

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

            if (_defaultLanguageStrings.TryGetValue(key, out string? defaultValue))
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
            _currentLanguageStrings = GetLanguageStrings(cultureName) ?? _defaultLanguageStrings;
        }

        if (onCultureChanged is not null)
        {
            OnLanguageChanged();
            NotifyAllPropertiesChanged();
            onCultureChanged?.Invoke();
        }
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