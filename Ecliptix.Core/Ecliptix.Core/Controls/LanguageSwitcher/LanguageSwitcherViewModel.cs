using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls.LanguageSwitcher;

public sealed class LanguageSwitcherViewModel : ReactiveObject, IActivatableViewModel
{
    private readonly ILocalizationService _localizationService;
    private readonly ISecureStorageProvider _secureStorageProvider;
    private readonly ObservableAsPropertyHelper<bool> _isEnglish;
    private readonly ObservableAsPropertyHelper<string> _currentLanguageCode;
    private readonly ObservableAsPropertyHelper<int> _activeSegmentIndex;
    private LanguageItem _selectedLanguage;

    public ViewModelActivator Activator { get; } = new();

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } =
    [
        new("en-US", "EN", "avares://Ecliptix.Core/Assets/Flags/usa_flag.svg"),
        new("uk-UA", "UK", "avares://Ecliptix.Core/Assets/Flags/ukraine_flag.svg")
        // Easy to add more:
        // new("de-DE", "DE", "avares://Ecliptix.Core/Assets/Flags/germany_flag.svg"),
        // new("fr-FR", "FR", "avares://Ecliptix.Core/Assets/Flags/france_flag.svg"),
        // new("es-ES", "ES", "avares://Ecliptix.Core/Assets/Flags/spain_flag.svg"),
    ];

    public bool IsEnglish => _isEnglish.Value;
    public string CurrentLanguageCode => _currentLanguageCode.Value;
    public int ActiveSegmentIndex => _activeSegmentIndex.Value;

    public LanguageItem SelectedLanguage
    {
        get => _selectedLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleLanguageCommand { get; }

    public LanguageSwitcherViewModel(ILocalizationService localizationService,
        ISecureStorageProvider secureStorageProvider)
    {
        _localizationService = localizationService;
        _secureStorageProvider = secureStorageProvider;
        _selectedLanguage = GetLanguageByCode(_localizationService.CurrentCultureName) ?? AvailableLanguages[0];

        IObservable<string> languageChanges = CreateLanguageObservable();

        _isEnglish = languageChanges
            .Select(culture => culture == "en-US")
            .ToProperty(this, x => x.IsEnglish);

        _currentLanguageCode = languageChanges
            .Select(culture => GetLanguageByCode(culture)?.DisplayName ?? "??")
            .ToProperty(this, x => x.CurrentLanguageCode);

        _activeSegmentIndex = languageChanges
            .Select(GetLanguageIndex)
            .ToProperty(this, x => x.ActiveSegmentIndex);

        ToggleLanguageCommand = ReactiveCommand.Create(ToggleLanguage);

        SetupReactiveBindings(languageChanges);
    }

    private LanguageItem? GetLanguageByCode(string cultureCode) =>
        AvailableLanguages.FirstOrDefault(lang => lang.Code == cultureCode);

    private int GetLanguageIndex(string cultureCode)
    {
        for (int i = 0; i < AvailableLanguages.Count; i++)
        {
            if (AvailableLanguages[i].Code == cultureCode)
            {
                return i;
            }
        }

        return 0;
    }

    private void ToggleLanguage()
    {
        int currentIndex = GetLanguageIndex(_localizationService.CurrentCultureName);
        int nextIndex = (currentIndex + 1) % AvailableLanguages.Count;
        _localizationService.SetCulture(AvailableLanguages[nextIndex].Code);
    }

    private IObservable<string> CreateLanguageObservable() =>
        Observable.Create<string>(observer =>
            {
                _localizationService.LanguageChanged += Handler;
                observer.OnNext(_localizationService.CurrentCultureName);

                return Disposable.Create(() => _localizationService.LanguageChanged -= Handler);

                void Handler() => observer.OnNext(_localizationService.CurrentCultureName);
            })
            .DistinctUntilChanged();

    private void SetupReactiveBindings(IObservable<string> languageChanges)
    {
        this.WhenActivated(disposables =>
        {
            languageChanges
                .Select(culture => GetLanguageByCode(culture) ?? AvailableLanguages[0])
                .BindTo(this, x => x.SelectedLanguage)
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.SelectedLanguage)
                .Where(item => item.Code != _localizationService.CurrentCultureName)
                .Subscribe(item => _localizationService.SetCulture(item.Code))
                .DisposeWith(disposables);
        });
    }
}