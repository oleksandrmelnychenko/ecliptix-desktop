using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Settings;
using Ecliptix.Utilities;

using ReactiveUI;

using Unit = System.Reactive.Unit;

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

        _selectedLanguage = GetLanguageByCode(_localizationService.CurrentCultureName) ??
                            LanguageConfig.SupportedLanguages.First();

        IObservable<string> languageChanges = CreateLanguageObservable();

        ToggleLanguageCommand = ReactiveCommand.Create(ToggleLanguage);

        _disposables.Add(ToggleLanguageCommand);

        SetupReactiveBindings(languageChanges);
    }

    private static LanguageItem? GetLanguageByCode(string cultureCode) =>
        LanguageConfig.GetLanguageByCode(cultureCode).ToNullable();

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
                    _localizationService.SetCulture(item.Code, () => HandleCultureChange(item.Code));
                })
                .DisposeWith(disposables);
        });
    }

    private void HandleCultureChange(string cultureCode)
    {
        _ = PersistCultureSettingAsync(cultureCode).ContinueWith(
            task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    Serilog.Log.Error(task.Exception, "[LANGUAGE-SELECTOR] Unhandled exception in culture persistence");
                }
            },
            System.Threading.Tasks.TaskScheduler.Default);

        _rpcMetaDataProvider.SetCulture(cultureCode);
    }

    private async System.Threading.Tasks.Task PersistCultureSettingAsync(string cultureCode)
    {
        try
        {
            Result<Utilities.Unit, InternalServiceApiFailure> result =
                await _applicationSecureStorageProvider
                    .SetApplicationSettingsCultureAsync(cultureCode).ConfigureAwait(false);

            if (result.IsErr)
            {
                Serilog.Log.Warning(
                    "[LANGUAGE-SELECTOR] Failed to persist culture setting. CULTURE: {CULTURE}, ERROR: {ERROR}",
                    cultureCode, result.UnwrapErr().Message);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[LANGUAGE-SELECTOR] Exception persisting culture setting. CULTURE: {CULTURE}",
                cultureCode);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ToggleLanguageCommand?.Dispose();
        _disposables.Dispose();

        _disposed = true;
    }
}
