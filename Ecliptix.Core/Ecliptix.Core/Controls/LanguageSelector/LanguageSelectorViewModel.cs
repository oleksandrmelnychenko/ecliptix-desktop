using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Services.Abstractions.Core;
using ReactiveUI;

namespace Ecliptix.Core.Controls.LanguageSelector;

public sealed class LanguageSelectorViewModel : ReactiveObject, IActivatableViewModel, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;
    private readonly CompositeDisposable _disposables = new();
    private LanguageItem _selectedLanguage;
    private bool _disposed;

    public ViewModelActivator Activator { get; } = new();

    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    public ObservableCollection<LanguageItem> AvailableLanguages { get; } =
        new(LanguageConfig.SupportedLanguages);

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

        _selectedLanguage = GetLanguageByCode(_localizationService.CurrentCultureName) ?? LanguageConfig.SupportedLanguages.First();

        IObservable<string> languageChanges = CreateLanguageObservable();

        ToggleLanguageCommand = ReactiveCommand.Create(ToggleLanguage);

        _disposables.Add(ToggleLanguageCommand);

        SetupReactiveBindings(languageChanges);
    }

    private static LanguageItem? GetLanguageByCode(string cultureCode) =>
        LanguageConfig.GetLanguageByCode(cultureCode);

    private static int GetLanguageIndex(string cultureCode) =>
        LanguageConfig.GetLanguageIndex(cultureCode);

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

    public void Dispose()
    {
        if (_disposed) return;

        ToggleLanguageCommand?.Dispose();
        _disposables.Dispose();

        _disposed = true;
    }
}