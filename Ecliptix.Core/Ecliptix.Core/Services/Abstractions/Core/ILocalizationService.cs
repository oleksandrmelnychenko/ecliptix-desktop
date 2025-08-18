using System;
using System.Globalization;
using System.ComponentModel;

namespace Ecliptix.Core.Services.Abstractions.Core;

public interface ILocalizationService : INotifyPropertyChanged
{
    string this[string key] { get; }
    void SetCulture(string? cultureName, Action? onCultureChanged = null);
    string GetString(string key, params object[] args);
    CultureInfo CurrentCultureInfo { get; }
    string CurrentCultureName { get; }
    event Action? LanguageChanged;
}