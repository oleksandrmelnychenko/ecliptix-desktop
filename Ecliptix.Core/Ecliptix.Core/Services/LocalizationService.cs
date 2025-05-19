using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Ecliptix.Core.Services;

public interface ILocalizationService
{
    string this[string key] { get; }
    void SetCulture(string cultureName);
    string CurrentCulture { get; }
    
    event Action? LanguageChanged;
}

public class LocalizationService : ILocalizationService
{
    private Dictionary<string, string> _localizedStrings = new();
    private string _currentCulture = "en-US";
    public event Action? LanguageChanged;
    public string CurrentCulture => _currentCulture;

    public LocalizationService()
    {
        LoadCulture(_currentCulture);
    }

    public string this[string key]
    {
        get
        {
            if (_localizedStrings.TryGetValue(key, out var value))
                return value;
            return $"!{key}!";
        }
    }

    public void SetCulture(string cultureName)
    {
        if (_currentCulture != cultureName)
        {
            LoadCulture(cultureName);
            _currentCulture = cultureName;
            LanguageChanged?.Invoke();
        }
    }

    private void LoadCulture(string cultureName)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var projectName = assembly.GetName().Name;
        var resourceName = $"{projectName}.Locales.{cultureName}.json";
        
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            _localizedStrings = new();
            return;
        }

        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        
        // Deserialize into JsonDocument for dynamic traversal
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        
        _localizedStrings = new Dictionary<string, string>();
        ProcessJsonElement(root, string.Empty, _localizedStrings);
    }

    private void ProcessJsonElement(JsonElement element, string path, Dictionary<string, string> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                string newPath = string.IsNullOrEmpty(path) ? property.Name : $"{path}.{property.Name}";
                ProcessJsonElement(property.Value, newPath, result);
            }
        }
        else if (element.ValueKind == JsonValueKind.String)
        {
            result[path] = element.GetString() ?? string.Empty;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                string newPath = $"{path}[{index}]";
                ProcessJsonElement(item, newPath, result);
                index++;
            }
        }
        else if (element.ValueKind == JsonValueKind.Number)
        {
            result[path] = element.GetRawText();
        }
        else if (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False)
        {
            result[path] = element.GetBoolean().ToString().ToLower();
        }
        else if (element.ValueKind == JsonValueKind.Null)
        {
            result[path] = string.Empty;
        }
    }
}