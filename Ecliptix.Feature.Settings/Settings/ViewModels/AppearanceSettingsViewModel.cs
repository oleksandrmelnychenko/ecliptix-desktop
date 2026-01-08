using System.Collections.ObjectModel;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Feature.Settings.Settings.ViewModels;

public class AppearanceSettingsViewModel : ReactiveObject
{
    public ObservableCollection<string> Languages { get; } = new()
    {
        "English (US)",
        "Ukrainian (UA)",
        "German (DE)",
        "French (FR)"
    };
    [Reactive] public string SelectedLanguage { get; set; }

    public ObservableCollection<string> FontSizes { get; } = new()
    {
        "Small (12px)",
        "Normal (14px)",
        "Large (16px)",
        "Extra Large (18px)"
    };
    [Reactive] public string SelectedFontSize { get; set; }

    public AppearanceSettingsViewModel()
    {
        SelectedLanguage = Languages[0];
        SelectedFontSize = FontSizes[1];

    }
}
