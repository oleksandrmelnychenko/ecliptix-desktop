using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Services.Generated;
using Microsoft.Extensions.Logging;

namespace Ecliptix.Core.Services;

public sealed class LocalizationService : ILocalizationService
{
    private IReadOnlyDictionary<string, string> _localizedStrings;
    private CultureInfo _currentCultureInfo;
    private readonly string _defaultCultureName;
    private readonly IReadOnlyDictionary<string, string> _defaultCultureStrings;

    private readonly ILogger<LocalizationService> _logger;
    private readonly Lock _cultureChangeLock = new();
    private bool _disposed;

    public event Action? LanguageChanged;

    public CultureInfo CurrentCultureInfo => _currentCultureInfo;
    public string CurrentCultureName => _currentCultureInfo.Name;

    public LocalizationService(ILogger<LocalizationService> logger, AppSettings appSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        LocalizationSettings locSettings = appSettings?.Localization ?? new LocalizationSettings();

        _defaultCultureName = locSettings.DefaultCulture;
        string initialCultureName = locSettings.InitialCulture;

        _logger.LogInformation(
            "LocalizationService initializing. Configured InitialCulture: {InitialCulture}, Configured DefaultCulture: {DefaultCulture}",
            initialCultureName, _defaultCultureName);

        if (GeneratedLocales.AllCultures.TryGetValue(_defaultCultureName, out var defaultStrings))
        {
            _defaultCultureStrings = defaultStrings;
            _logger.LogInformation("Default culture '{DefaultCulture}' loaded. Strings: {Count}", _defaultCultureName,
                defaultStrings.Count);
        }
        else
        {
            _logger.LogWarning(
                "Default culture '{DefaultCulture}' not found in generated locales. Default fallback will be empty.",
                _defaultCultureName);
            _defaultCultureStrings = new Dictionary<string, string>(); // Ensure not null
        }

        if (GeneratedLocales.AllCultures.TryGetValue(initialCultureName, out var initialStrings))
        {
            _localizedStrings = initialStrings;
            _currentCultureInfo = CreateCultureInfo(initialCultureName);
            _logger.LogInformation("Initial culture '{InitialCulture}' loaded successfully.", initialCultureName);
        }
        else
        {
            _logger.LogWarning(
                "Initial culture '{InitialCulture}' not found in generated locales. Falling back to default culture '{DefaultCulture}'.",
                initialCultureName, _defaultCultureName);
            _localizedStrings = _defaultCultureStrings;
            _currentCultureInfo = CreateCultureInfo(_defaultCultureName);
        }
    }

    private CultureInfo CreateCultureInfo(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException ex)
        {
            _logger.LogError(ex, "Culture '{CultureName}' is not a valid culture. Using InvariantCulture as fallback.",
                cultureName);
            return CultureInfo.InvariantCulture;
        }
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                _logger.LogWarning("Attempted to retrieve a localization string with a null or empty key.");
                return "[INVALID_KEY]"; // Or throw ArgumentNullException
            }

            if (_localizedStrings.TryGetValue(key, out string? value))
            {
                return value;
            }

            if (_defaultCultureStrings.TryGetValue(key, out var defaultValue))
            {
                _logger.LogDebug(
                    "Key '{Key}' not found in current culture '{CurrentCulture}', using from default '{DefaultCulture}'.",
                    key, _currentCultureInfo.Name, _defaultCultureName);
                return defaultValue;
            }

            _logger.LogWarning(
                "Localization key not found: '{Key}' for culture '{CurrentCulture}' and no default value exists.", key,
                _currentCultureInfo.Name);
            return $"!{key}!"; // Key not found marker
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
            _logger.LogWarning(ex,
                "Error formatting localization key '{Key}' for culture '{CurrentCulture}'. Args count: {ArgCount}. Format string: '{FormatString}'",
                key, _currentCultureInfo.Name, args.Length, formatString);
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
                _logger.LogDebug("Requested culture '{CultureName}' is already the current culture. No change made.",
                    newCultureInfo.Name);
                return;
            }

            _logger.LogInformation("Attempting to set culture from '{OldCulture}' to '{NewCulture}'",
                _currentCultureInfo.Name, newCultureInfo.Name);
            if (GeneratedLocales.AllCultures.TryGetValue(newCultureInfo.Name, out var newCultureStrings))
            {
                _localizedStrings = newCultureStrings;
                _currentCultureInfo = newCultureInfo;

                _logger.LogInformation(
                    "Culture changed successfully to '{NewCulture}'. Invoking LanguageChanged event.",
                    newCultureInfo.Name);
                OnLanguageChanged();
            }
            else
            {
                _logger.LogError(
                    "Failed to set culture to '{CultureName}'. Data not found in generated locales. Current culture '{CurrentCulture}' remains unchanged.",
                    newCultureInfo.Name, _currentCultureInfo.Name);
            }
        }
    }

    private void OnLanguageChanged() =>
        LanguageChanged?.Invoke();
}