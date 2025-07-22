using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ecliptix.Core.Services;

namespace Ecliptix.Core.Controls.LanguageSwitcher;

public partial class LanguageSwitcherViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;

    [ObservableProperty] private bool _isEnglish = true;

    [ObservableProperty] private LanguageItem _selectedLanguage;

    public ObservableCollection<LanguageItem> AvailableLanguages { get; }

    public string CurrentLanguageCode => IsEnglish ? "EN" : "UA";
    public string CurrentLanguageFlag => IsEnglish ? "🇺🇸" : "🇺🇦";
    public int ActiveSegmentIndex => IsEnglish ? 0 : 1;

    public ICommand ToggleLanguageCommand { get; }
    public ICommand SetEnglishCommand { get; }
    public ICommand SetUkrainianCommand { get; }

    public LanguageSwitcherViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;

        AvailableLanguages =
        [
            new LanguageItem("en-US", "English", "🇺🇸"),
            new LanguageItem("uk-UA", "Українська", "🇺🇦")
        ];

        _selectedLanguage = AvailableLanguages[0];
        IsEnglish = _localizationService.CurrentCultureName == "en-US";

        ToggleLanguageCommand = new RelayCommand(ToggleLanguage);
        SetEnglishCommand = new RelayCommand(SetEnglish);
        SetUkrainianCommand = new RelayCommand(SetUkrainian);

        _localizationService.LanguageChanged += OnLanguageChanged;
    }

    private void ToggleLanguage()
    {
        if (IsEnglish)
        {
            SetUkrainian();
        }
        else
        {
            SetEnglish();
        }
    }

    private void SetEnglish()
    {
        if (!IsEnglish)
        {
            _localizationService.SetCulture("en-US");
            IsEnglish = true;
            SelectedLanguage = AvailableLanguages[0];
            OnPropertyChanged(nameof(CurrentLanguageCode));
            OnPropertyChanged(nameof(CurrentLanguageFlag));
            OnPropertyChanged(nameof(ActiveSegmentIndex));
        }
    }

    private void SetUkrainian()
    {
        if (IsEnglish)
        {
            _localizationService.SetCulture("uk-UA");
            IsEnglish = false;
            SelectedLanguage = AvailableLanguages[1];
            OnPropertyChanged(nameof(CurrentLanguageCode));
            OnPropertyChanged(nameof(CurrentLanguageFlag));
            OnPropertyChanged(nameof(ActiveSegmentIndex));
        }
    }

    private void OnLanguageChanged()
    {
        IsEnglish = _localizationService.CurrentCultureName == "en-US";
        OnPropertyChanged(nameof(CurrentLanguageCode));
        OnPropertyChanged(nameof(CurrentLanguageFlag));
        OnPropertyChanged(nameof(ActiveSegmentIndex));
    }

    partial void OnSelectedLanguageChanged(LanguageItem value)
    {
        if (value != null)
        {
            _localizationService.SetCulture(value.Code);
        }
    }
}

public record LanguageItem(string Code, string DisplayName, string Flag);