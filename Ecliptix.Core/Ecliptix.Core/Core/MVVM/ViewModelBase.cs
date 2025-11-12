using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.SourceGenerators;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Core.MVVM;

public abstract partial class ViewModelBase : ReactiveObject, IDisposable, IActivatableViewModel
{
    private bool _disposedValue;
    private ObservableAsPropertyHelper<bool>? _connectivitySubscription;

    protected ViewModelBase(NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IConnectivityService? connectivityService = null)
    {
        NetworkProvider = networkProvider;
        LocalizationService = localizationService;

        LanguageChanged = Observable.FromEvent(
                handler => localizationService.LanguageChanged += handler,
                handler => localizationService.LanguageChanged -= handler
            )
            .StartWith(SystemU.Default)
            .Publish()
            .RefCount();

        if (connectivityService != null)
        {
            IObservable<bool> networkStatusStream = connectivityService.ConnectivityStream
                .Select(IsNetworkInOutage)
                .StartWith(IsNetworkInOutage(connectivityService.CurrentSnapshot))
                .DistinctUntilChanged();

            _connectivitySubscription = networkStatusStream.ToProperty(this, x => x.IsInNetworkOutage);
        }

        this.WhenActivated(disposables =>
        {
            LanguageChanged
                .Do(_ => this.RaisePropertyChanged(string.Empty))
                .Subscribe()
                .DisposeWith(disposables);
        });
    }

    public ILocalizationService LocalizationService { get; }
    public ViewModelActivator Activator { get; } = new();

    [ObservableAsProperty] public bool IsInNetworkOutage { get; }

    protected NetworkProvider NetworkProvider { get; }
    protected IObservable<SystemU> LanguageChanged { get; }

    protected uint ComputeConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(NetworkProvider.ApplicationInstanceSettings,
                pubKeyExchangeType);

        return connectId;
    }

    protected Membership Membership() =>
        NetworkProvider.ApplicationInstanceSettings.Membership;

    public string GetLocalizedWarningMessage(CharacterWarningType warningType)
    {
        return warningType switch
        {
            CharacterWarningType.NON_LATIN_LETTER => LocalizationService["ValidationWarnings.SecureKey.NonLatinLetter"],
            CharacterWarningType.INVALID_CHARACTER => LocalizationService[
                "ValidationWarnings.SecureKey.InvalidCharacter"],
            CharacterWarningType.MULTIPLE_CHARACTERS => LocalizationService[
                "ValidationWarnings.SecureKey.MultipleCharacters"],
            _ => LocalizationService["ValidationWarnings.SecureKey.InvalidCharacter"]
        };
    }

    protected void ShowServerErrorNotification(AuthenticationViewModel hostWindow, string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return;
        }

        UserRequestErrorViewModel errorViewModel = new(errorMessage, LocalizationService);
        UserRequestErrorView errorView = new() { DataContext = errorViewModel };

        Task.Run(async () =>
        {
            try
            {
                await hostWindow.ShowBottomSheet(
                    BottomSheetComponentType.USER_REQUEST_ERROR,
                    errorView,
                    showScrim: false,
                    isDismissable: true);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[VIEWMODEL-BASE] Exception showing user request error bottom sheet");
            }
        }).ContinueWith(
            task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    Serilog.Log.Error(task.Exception, "[VIEWMODEL-BASE] Unhandled exception in ShowServerErrorNotification background task");
                }
            },
            TaskScheduler.Default);
    }

    protected void ShowRedirectNotification(AuthenticationViewModel hostWindow, string message, int seconds,
        Action onComplete)
    {
        if (_disposedValue)
        {
            onComplete();
            return;
        }

        RedirectNotificationViewModel redirectViewModel = new(message, seconds, onComplete, LocalizationService);
        RedirectNotificationView redirectView = new() { DataContext = redirectViewModel };

        Task.Run(async () =>
        {
            try
            {
                if (!_disposedValue)
                {
                    await hostWindow.ShowBottomSheet(BottomSheetComponentType.REDIRECT_NOTIFICATION, redirectView,
                        showScrim: true, isDismissable: false);
                }
                else
                {
                    onComplete();
                }
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[VIEWMODEL-BASE] Exception showing redirect notification bottom sheet");
                onComplete();
            }
        }).ContinueWith(
            task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    Serilog.Log.Error(task.Exception, "[VIEWMODEL-BASE] Unhandled exception in ShowRedirectNotification background task");
                    onComplete();
                }
            },
            TaskScheduler.Default);
    }

    protected static void CleanupAndNavigate(AuthenticationViewModel membershipHostWindow, MembershipViewType targetView)
    {
        membershipHostWindow.ClearNavigationStack();
        membershipHostWindow.Navigate.Execute(targetView).Subscribe();

        Task.Run(async () =>
        {
            try
            {
                await membershipHostWindow.HideBottomSheetAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[VIEWMODEL-BASE] Exception hiding bottom sheet during cleanup");
            }
        }).ContinueWith(
            task =>
            {
                if (task.IsFaulted && task.Exception != null)
                {
                    Serilog.Log.Error(task.Exception, "[VIEWMODEL-BASE] Unhandled exception in CleanupAndNavigate background task");
                }
            },
            TaskScheduler.Default);
    }

    protected string GetSecureKeyLocalization(AuthenticationFlowContext flowContext, string registrationKey, string recoveryKey) => flowContext switch
    {
        AuthenticationFlowContext.REGISTRATION => LocalizationService[registrationKey],
        AuthenticationFlowContext.SECURE_KEY_RECOVERY => LocalizationService[recoveryKey],
        _ => LocalizationService[registrationKey]
    };

    private static bool IsNetworkInOutage(ConnectivitySnapshot snapshot) =>
        snapshot.Status is ConnectivityStatus.DISCONNECTED
            or ConnectivityStatus.SHUTTING_DOWN
            or ConnectivityStatus.RECOVERING
            or ConnectivityStatus.RETRIES_EXHAUSTED
            or ConnectivityStatus.UNAVAILABLE;

    protected static CancellationTokenSource RecreateCancellationToken(ref CancellationTokenSource? cts)
    {
        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VIEWMODEL-BASE] CTS already disposed during recreation: {ex.Message}");
        }

        cts?.Dispose();
        cts = new CancellationTokenSource();
        return cts;
    }

    protected static CancellationTokenSource RecreateCancellationToken(ref Option<CancellationTokenSource> ctsOption)
    {
        ctsOption.Do(cts =>
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VIEWMODEL-BASE] CTS already disposed during recreation (Option): {ex.Message}");
            }
            cts.Dispose();
        });

        CancellationTokenSource newCts = new CancellationTokenSource();
        ctsOption = Option<CancellationTokenSource>.Some(newCts);
        return newCts;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue)
        {
            return;
        }

        if (disposing)
        {
            _connectivitySubscription?.Dispose();
            _connectivitySubscription = null;
        }

        _disposedValue = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
