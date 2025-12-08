using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.LanguageSelector;
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

public record LanguageItemViewModel(string Code, string EnglishName, string NativeName);


public class SelectableLanguageViewModel : ReactiveObject
{
    public LanguageItem Model { get; }

    [Reactive]
    public bool IsSelected { get; set; }

    public SelectableLanguageViewModel(LanguageItem model, bool isSelected)
    {
        Model = model;
        IsSelected = isSelected;
    }
}

public class LanguageSelectionViewModel : ReactiveObject, IActivatableViewModel
{
    private readonly ISideSheetService _sideSheetService;
    private readonly ILocalizationService _localizationService;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;

    public ViewModelActivator Activator { get; } = new();

    public ObservableCollection<SelectableLanguageViewModel> Languages { get; }

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }

    public ReactiveCommand<SelectableLanguageViewModel, Unit> SelectLanguageCommand { get; }

    public LanguageSelectionViewModel(
        ISideSheetService sideSheetService,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider)
    {
        _sideSheetService = sideSheetService;
        _localizationService = localizationService;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _rpcMetaDataProvider = rpcMetaDataProvider;

        string currentCulture = _localizationService.CurrentCultureName;

        IEnumerable<SelectableLanguageViewModel> selectableLanguages = AppCultureSettings.Default.SupportedLanguages
            .Select(lang => new SelectableLanguageViewModel(lang, lang.Code == currentCulture));

        Languages = new ObservableCollection<SelectableLanguageViewModel>(selectableLanguages);

        CloseCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _sideSheetService.HideAsync();
        });

        SelectLanguageCommand = ReactiveCommand.CreateFromTask<SelectableLanguageViewModel>(async (selectedItem) =>
        {
            await SelectLanguageAsync(selectedItem);
        });
    }

    private async Task SelectLanguageAsync(SelectableLanguageViewModel selectedItem)
    {
        if (selectedItem is null)
        {
            return;
        }

        if (_localizationService.CurrentCultureName == selectedItem.Model.Code)
        {
            await _sideSheetService.HideAsync();
            return;
        }

        _localizationService.SetCulture(selectedItem.Model.Code, () =>
        {
            HandleCultureChange(selectedItem.Model.Code);
        });

        await _sideSheetService.HideAsync();
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
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[LANGUAGE-SELECTION] Exception persisting culture setting. CULTURE: {Culture}",
                cultureCode);
        }
    }
}
