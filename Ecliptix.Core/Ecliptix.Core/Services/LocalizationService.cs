using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using Avalonia.Threading;
using Ecliptix.Core.Settings;

namespace Ecliptix.Core.Services;

public sealed class LocalizationService : ILocalizationService
{
    private readonly Dictionary<string, string> _localizedStrings;
    private CultureInfo _currentCultureInfo;
    private readonly Dictionary<string, string> _defaultCultureStrings;
    private readonly Assembly _assembly;
    private readonly string _resourceNamespace;

    private readonly Lock _cultureChangeLock = new();

    public event Action? LanguageChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo CurrentCultureInfo => _currentCultureInfo;
    public string CurrentCultureName => _currentCultureInfo.Name;

    public LocalizationService(DefaultSystemSettings defaultSystemSettings)
    {
        string defaultCultureName = defaultSystemSettings.Culture;

        _assembly = Assembly.GetExecutingAssembly();
        _resourceNamespace = "Ecliptix.Core.Localization";

        _defaultCultureStrings = new Dictionary<string, string>();
        _localizedStrings = new Dictionary<string, string>();
        _currentCultureInfo = CreateCultureInfo(defaultCultureName);

        LoadDefaultCulture();
        LoadCulture(defaultCultureName);
    }

    private static CultureInfo CreateCultureInfo(string cultureName)
    {
        try
        {
            return CultureInfo.GetCultureInfo(cultureName);
        }
        catch (CultureNotFoundException)
        {
            return CultureInfo.InvariantCulture;
        }
    }

    private void LoadDefaultCulture()
    {
        string resourceName = $"{_resourceNamespace}.en-US.json";
        LoadEmbeddedJsonFile(resourceName, _defaultCultureStrings);
    }

    private void LoadCulture(string cultureName)
    {
        string resourceName = $"{_resourceNamespace}.{cultureName}.json";
        LoadEmbeddedJsonFile(resourceName, _localizedStrings);
    }

    private void LoadEmbeddedJsonFile(string resourceName, Dictionary<string, string> targetDictionary)
    {
        using Stream? stream = _assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return;
        }

        using StreamReader reader = new(stream);
        string jsonContent = reader.ReadToEnd();

        JsonDocument document = JsonDocument.Parse(jsonContent);
        FlattenJsonObject(document.RootElement, string.Empty, targetDictionary);
    }

    private static void FlattenJsonObject(JsonElement element, string prefix, Dictionary<string, string> result)
    {
        foreach (JsonProperty property in element.EnumerateObject())
        {
            string key = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";

            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                FlattenJsonObject(property.Value, key, result);
            }
            else if (property.Value.ValueKind == JsonValueKind.String)
            {
                result[key] = property.Value.GetString() ?? string.Empty;
            }
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

            if (_defaultCultureStrings.TryGetValue(key, out string? defaultValue))
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

    public void SetCulture(string cultureName, Action? onCultureChanged = null)
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
            _localizedStrings.Clear();
            LoadCulture(cultureName);

           
        }

        if (onCultureChanged is not null)
        {
            onCultureChanged?.Invoke();
            OnLanguageChanged();
            NotifyAllPropertiesChanged();
        }
    }

    private void OnLanguageChanged()
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            LanguageChanged.Invoke();
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
            PropertyChanged.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }
        else
        {
            Dispatcher.UIThread.Post(() =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")));
        }
    }
}