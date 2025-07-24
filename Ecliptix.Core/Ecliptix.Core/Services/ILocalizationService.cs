using System;
using System.Globalization;
using System.ComponentModel;

namespace Ecliptix.Core.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
    string this[string key] { get; }
    void SetCulture(string cultureName, Action? onCultureChanged = null);
    CultureInfo CurrentCultureInfo { get; }
    string CurrentCultureName { get; }
    event Action? LanguageChanged;
}