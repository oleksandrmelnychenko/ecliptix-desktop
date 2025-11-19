using System.Collections.ObjectModel;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;

using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Settings.ViewModels;

public class AppearanceSettingsViewModel : ReactiveObject
{
    // Теми
    public ObservableCollection<string> Themes { get; } = new()
    {
        "System Default",
        "Light Mode",
        "Dark Mode",
        "High Contrast"
    };
    [Reactive] public string SelectedTheme { get; set; }

    // Мови
    public ObservableCollection<string> Languages { get; } = new()
    {
        "English (US)",
        "Ukrainian (UA)",
        "German (DE)",
        "French (FR)"
    };
    [Reactive] public string SelectedLanguage { get; set; }

    // Розмір шрифту
    public ObservableCollection<string> FontSizes { get; } = new()
    {
        "Small (12px)",
        "Normal (14px)",
        "Large (16px)",
        "Extra Large (18px)"
    };
    [Reactive] public string SelectedFontSize { get; set; }

    // Команда скидання
    public ReactiveCommand<SystemU, SystemU> ResetDefaultsCommand { get; }

    public AppearanceSettingsViewModel()
    {
        // Значення за замовчуванням
        SelectedTheme = Themes[0];
        SelectedLanguage = Languages[0];
        SelectedFontSize = FontSizes[1];

        ResetDefaultsCommand = ReactiveCommand.Create(() =>
        {
            SelectedTheme = Themes[0];
            SelectedLanguage = Languages[0];
            SelectedFontSize = FontSizes[1];
        });
    }
}
