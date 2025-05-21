using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication;

public class AuthenticationViewModel : ReactiveObject
{
    private UserControl? _currentView;

    private readonly ILocalizationService _localizationService;
    
    public ICommand SwitchToEnglishCommand { get; }
    public ICommand SwitchToUkrainianCommand { get; }
    
    public ObservableCollection<LanguageOption> Languages { get; }
    
    private LanguageOption? _selectedLanguage;
    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
            if (value != null)
                _localizationService.SetCulture(value.Culture);
        }
    }
        
    public UserControl? CurrentView
    {
        get => _currentView;
        private set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    public IReadOnlyList<AuthViewType> MenuItems { get; }
        = Enum.GetValues<AuthViewType>();

    public ReactiveCommand<AuthViewType, Unit> ShowView { get; }

    public AuthenticationViewModel(
        AuthenticationViewFactory authenticationViewFactory,
        ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        
        SwitchToEnglishCommand = new RelayCommand(SwitchToEnglish);
        SwitchToUkrainianCommand = new RelayCommand(SwitchToUkrainian);
        
        Languages =
        [
            new LanguageOption("EN", "en-US", "/Assets/us.svg"),
            new LanguageOption("UA", "uk-UA", "/Assets/ua.svg")
        ];
        
        SelectedLanguage = Languages[0];
        
        ShowView = ReactiveCommand.Create<AuthViewType>(type =>
        {
            CurrentView = authenticationViewFactory.Create(type);
        });

        ShowView.Execute(AuthViewType.ConfirmPassword).Subscribe();
    }

    private void SwitchToUkrainian() => _localizationService.SetCulture("uk-UA");
    private void SwitchToEnglish() => _localizationService.SetCulture("en-US");

}

public class LanguageOption(string displayName, string culture, string flagIcon)
{
    public string DisplayName { get; } = displayName;
    public string Culture { get; } = culture;
    public string FlagIcon { get; } = flagIcon;
}