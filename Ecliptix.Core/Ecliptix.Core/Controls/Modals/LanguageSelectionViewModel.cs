using System;
using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public record LanguageItemViewModel(string Code, string EnglishName, string NativeName);

public class LanguageSelectionViewModel : ReactiveObject
{
    public ObservableCollection<LanguageItemViewModel> Languages { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public LanguageSelectionViewModel()
    {
        Languages = new ObservableCollection<LanguageItemViewModel>
        {
            new("EN", "English", "English"),
            new("UA", "Ukrainian", "Українська"),
            new("DE", "German", "Deutsch"),
            new("PL", "Polish", "Polski"),
            new("FR", "French", "Français"),
            new("ES", "Spanish", "Español"),
            new("IT", "Italian", "Italiano")
        };

        CloseCommand = ReactiveCommand.Create(() =>
        {
            Console.WriteLine("Close button pressed");
        });
    }
}
