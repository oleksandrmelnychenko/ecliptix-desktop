using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Configuration;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.LanguageSelector;

public sealed class LanguageSelectorViewModel : ReactiveObject, IActivatableViewModel
{
    private readonly ILocalizationService _localizationService;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;
    private LanguageItem _selectedLanguage;

    public ViewModelActivator Activator { get; } = new();

    private static readonly FrozenDictionary<string, LanguageItem> LanguageCodeMap = LanguageConfiguration.LanguageCodeMap;

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } =
        new(LanguageCodeMap.Values);

    public LanguageItem SelectedLanguage
    {
        get => _selectedLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
    }

    public ReactiveCommand<Unit, Unit> ToggleLanguageCommand { get; }

    public LanguageSelectorViewModel(ILocalizationService localizationService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider)
    {
        _localizationService = localizationService;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _rpcMetaDataProvider = rpcMetaDataProvider;

        _selectedLanguage = GetLanguageByCode(_localizationService.CurrentCultureName) ?? LanguageCodeMap.Values.First();

        IObservable<string> languageChanges = CreateLanguageObservable();

        ToggleLanguageCommand = ReactiveCommand.Create(ToggleLanguage);

        SetupReactiveBindings(languageChanges);
    }

    private static LanguageItem? GetLanguageByCode(string cultureCode) =>
        LanguageConfiguration.GetLanguageByCode(cultureCode);

    private static readonly FrozenDictionary<string, int> LanguageIndexMap = LanguageConfiguration.LanguageIndexMap;

    private static int GetLanguageIndex(string cultureCode) =>
        LanguageConfiguration.GetLanguageIndex(cultureCode);

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
                            _applicationSecureStorageProvider.SetApplicationSettingsCultureAsync(item.Code);
                            _rpcMetaDataProvider.SetCulture(item.Code);
                        });
                })
                .DisposeWith(disposables);
        });
    }
}