using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Settings;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Controls.Modals;

public class LanguagePickerItemViewModel(LanguageItem model, bool isSelected) : ReactiveObject
{
    public LanguageItem Model { get; } = model;

    [Reactive]
    public bool IsSelected { get; set; } = isSelected;
}

public class LanguagePickerViewModel : ReactiveObject, IActivatableViewModel, IDisposable
{
    private readonly IGlobalModalService _globalModalService;
    private readonly ILocalizationService _localizationService;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;
    private bool _isDisposed;

    public ViewModelActivator Activator { get; } = new();

    public ILocalizationService LocalizationService => _localizationService;

    public ObservableCollection<LanguagePickerItemViewModel> Languages { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ReactiveCommand<LanguagePickerItemViewModel, Unit> SelectLanguageCommand { get; }

    public LanguagePickerViewModel(
        IGlobalModalService globalModalService,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider)
    {
        _globalModalService = globalModalService;
        _localizationService = localizationService;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _rpcMetaDataProvider = rpcMetaDataProvider;

        string currentCulture = _localizationService.CurrentCultureName;

        IEnumerable<LanguagePickerItemViewModel> selectableLanguages = AppCultureSettings.Default.SupportedLanguages
            .Select(lang => new LanguagePickerItemViewModel(lang, lang.Code == currentCulture));

        Languages = new ObservableCollection<LanguagePickerItemViewModel>(selectableLanguages);

        CloseCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _globalModalService.CloseAllAsync();
        });

        SelectLanguageCommand = ReactiveCommand.CreateFromTask<LanguagePickerItemViewModel>(async (selectedItem) =>
        {
            await SelectLanguageAsync(selectedItem);
        });
    }

    private async Task SelectLanguageAsync(LanguagePickerItemViewModel selectedItem)
    {
        if (selectedItem == null || selectedItem.Model == null)
        {
            return;
        }

        if (_localizationService.CurrentCultureName == selectedItem.Model.Code)
        {
            await _globalModalService.CloseAllAsync();
            return;
        }

        _localizationService.SetCulture(selectedItem.Model.Code, () =>
        {
            HandleCultureChange(selectedItem.Model.Code);
        });

        await _globalModalService.CloseAllAsync();
    }

    private void HandleCultureChange(string cultureCode)
    {
        _rpcMetaDataProvider.SetCulture(cultureCode);

        _ = PersistCultureSettingAsync(cultureCode).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Serilog.Log.Error(task.Exception, "[LANGUAGE-SELECTION] Unhandled exception in culture persistence");
                }
            },
            TaskScheduler.Default);
    }

    private async Task PersistCultureSettingAsync(string cultureCode)
    {
        try
        {
            Result<Ecliptix.Utilities.Unit, InternalServiceApiFailure> result = await _applicationSecureStorageProvider
                    .SetApplicationSettingsCultureAsync(cultureCode).ConfigureAwait(false);

            if (result.IsErr)
            {
                Serilog.Log.Warning(
                    "[LANGUAGE-SELECTION] Failed to persist culture setting. CULTURE: {Culture}, ERROR: {Error}",
                    cultureCode, result.UnwrapErr().Message);
            }
        }
        catch (Exception)
        {
            // ignored
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        CloseCommand.Dispose();
        SelectLanguageCommand.Dispose();
    }
}
