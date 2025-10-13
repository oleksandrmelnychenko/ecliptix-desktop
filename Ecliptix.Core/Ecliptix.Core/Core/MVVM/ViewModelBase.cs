using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using ReactiveUI;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Core.MVVM;

public abstract class ViewModelBase : ReactiveObject, IDisposable, IActivatableViewModel
{
    public ILocalizationService LocalizationService { get; }

    protected ISystemEventService SystemEventService { get; }
    protected NetworkProvider NetworkProvider { get; }

    private bool _disposedValue;

    public ViewModelActivator Activator { get; } = new();

    protected IObservable<SystemU> LanguageChanged { get; }

    protected ViewModelBase(ISystemEventService systemEventService, NetworkProvider networkProvider,
        ILocalizationService localizationService)
    {
        SystemEventService = systemEventService;
        NetworkProvider = networkProvider;
        LocalizationService = localizationService;

        LanguageChanged = Observable.FromEvent(
                handler => localizationService.LanguageChanged += handler,
                handler => localizationService.LanguageChanged -= handler
            )
            .StartWith(SystemU.Default)
            .Publish()
            .RefCount();

        this.WhenActivated(disposables =>
        {
            LanguageChanged
                .Do(_ => this.RaisePropertyChanged(string.Empty))
                .Subscribe()
                .DisposeWith(disposables);
        });
    }

    protected uint ComputeConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(NetworkProvider.ApplicationInstanceSettings,
                pubKeyExchangeType);

        return connectId;
    }

    protected string SystemDeviceIdentifier() =>
        NetworkProvider.ApplicationInstanceSettings.SystemDeviceIdentifier;

    protected Membership Membership() =>
        NetworkProvider.ApplicationInstanceSettings.Membership;

    public string GetLocalizedWarningMessage(CharacterWarningType warningType)
    {
        return warningType switch
        {
            CharacterWarningType.NonLatinLetter => LocalizationService["ValidationWarnings.SecureKey.NonLatinLetter"],
            CharacterWarningType.InvalidCharacter => LocalizationService[
                "ValidationWarnings.SecureKey.InvalidCharacter"],
            CharacterWarningType.MultipleCharacters => LocalizationService[
                "ValidationWarnings.SecureKey.MultipleCharacters"],
            _ => LocalizationService["ValidationWarnings.SecureKey.InvalidCharacter"]
        };
    }

    protected void ShowServerErrorNotification(MembershipHostWindowModel hostWindow, string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage)) return;

        UserRequestErrorViewModel errorViewModel = new(errorMessage, LocalizationService);
        UserRequestErrorView errorView = new() { DataContext = errorViewModel };

        _ = Task.Run(async () =>
        {
            await hostWindow.ShowBottomSheet(
                BottomSheetComponentType.UserRequestError,
                errorView,
                showScrim: false,
                isDismissable: true);
        });
    }

    protected void ShowRedirectNotification(MembershipHostWindowModel hostWindow, string message, int seconds, Action onComplete)
    {
        if (_disposedValue)
        {
            onComplete();
            return;
        }

        RedirectNotificationViewModel redirectViewModel = new(message, seconds, onComplete, LocalizationService);
        RedirectNotificationView redirectView = new() { DataContext = redirectViewModel };

        _ = Task.Run(async () =>
        {
            if (!_disposedValue)
                await hostWindow.ShowBottomSheet(BottomSheetComponentType.RedirectNotification, redirectView, showScrim: true, isDismissable: false);
            else
                onComplete();
        });
    }

    protected void CleanupAndNavigate(MembershipHostWindowModel membershipHostWindow, MembershipViewType targetView)
    {
        membershipHostWindow.Navigate.Execute(targetView).Subscribe();
        membershipHostWindow.ClearNavigationStack();

        _ = Task.Run(async () =>
        {
            await membershipHostWindow.HideBottomSheetAsync();

        });
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
    }
}
