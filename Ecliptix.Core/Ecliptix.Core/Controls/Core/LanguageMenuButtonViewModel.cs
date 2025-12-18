using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Controls.Core;

public class LanguageMenuButtonViewModel : ReactiveObject, IActivatableViewModel, IDisposable
{
    private readonly LanguagePickerViewModel _cachedLanguagePickerVm;
    private readonly ILocalizationService _localizationService;
    private bool _isDisposed;

    public ViewModelActivator Activator { get; } = new();

    public ReactiveCommand<Unit, Unit> OpenLanguagePickerCommand { get; }

    [Reactive] public LanguageItem? CurrentLanguage { get; set; }

    public LanguageMenuButtonViewModel(
        ISideSheetService sideSheetService,
        IApplicationSecureStorageProvider storageProvider,
        ILocalizationService localizationService,
        IRpcMetaDataProvider rpcMetaDataProvider
        )
    {
        _localizationService = localizationService;

        _cachedLanguagePickerVm = new LanguagePickerViewModel(
            sideSheetService,
            localizationService,
            storageProvider,
            rpcMetaDataProvider);

        OpenLanguagePickerCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await sideSheetService.ShowAsync(
                _cachedLanguagePickerVm,
                showScrim: true,
                isDismissable: true
            );
        });

        UpdateCurrentLanguage(_localizationService.CurrentCultureName);

        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    h => _localizationService.LanguageChanged += h,
                    h => _localizationService.LanguageChanged -= h)
                .Subscribe(_ =>
                {
                    UpdateCurrentLanguage(_localizationService.CurrentCultureName);
                })
                .DisposeWith(disposables);
        });
    }

    private void UpdateCurrentLanguage(string code)
    {
        Option<LanguageItem> langOption = AppCultureSettings.Default.GetLanguageByCode(code);

        if (langOption.IsSome)
        {
            CurrentLanguage = langOption.Value;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        OpenLanguagePickerCommand?.Dispose();
        _cachedLanguagePickerVm?.Dispose();
    }
}
