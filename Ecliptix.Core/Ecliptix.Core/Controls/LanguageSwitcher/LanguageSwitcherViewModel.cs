using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.AppDevice;
using ReactiveUI;

namespace Ecliptix.Core.Controls.LanguageSwitcher;

public sealed class LanguageSwitcherViewModel : ReactiveObject, IActivatableViewModel
{
    private readonly ILocalizationService _localizationService;
    private readonly ISecureStorageProvider _secureStorageProvider;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;
    private LanguageItem _selectedLanguage;

    public ViewModelActivator Activator { get; } = new();

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } =
    [
        new("en-US", "EN", "avares://Ecliptix.Core/Assets/Flags/usa_flag.svg"),
        new("uk-UA", "UK", "avares://Ecliptix.Core/Assets/Flags/ukraine_flag.svg")
    ];

    public LanguageItem SelectedLanguage
    {
        get => _selectedLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleLanguageCommand { get; }

    public LanguageSwitcherViewModel(ILocalizationService localizationService,
        ISecureStorageProvider secureStorageProvider, IRpcMetaDataProvider rpcMetaDataProvider)
    {
        _localizationService = localizationService;
        _secureStorageProvider = secureStorageProvider;
        _rpcMetaDataProvider = rpcMetaDataProvider;

        _selectedLanguage = GetLanguageByCode(_localizationService.CurrentCultureName) ?? AvailableLanguages[0];

        IObservable<string> languageChanges = CreateLanguageObservable();

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
                .Subscribe(item =>
                {
                    _localizationService.SetCulture(item.Code,
                        () =>
                        {
                            _secureStorageProvider.SetApplicationSettingsCultureAsync(item.Code);
                            _rpcMetaDataProvider.SetCulture(item.Code);
                        });
                })
                .DisposeWith(disposables);
        });
    }
}