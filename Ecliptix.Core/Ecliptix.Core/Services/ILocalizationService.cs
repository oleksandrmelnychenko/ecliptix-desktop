using System;
using System.Globalization;

namespace Ecliptix.Core.Services;

public interface ILocalizationService
{
    string this[string key] { get; }
    string GetString(string key, params object[] args);
    void SetCulture(string cultureName);
    CultureInfo CurrentCultureInfo { get; }
    string CurrentCultureName { get; }
    event Action? LanguageChanged;
}