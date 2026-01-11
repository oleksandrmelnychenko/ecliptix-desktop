using System;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity.Authentication;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Services.Localization;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.MVVM;

public abstract class ViewModelBase : ReactiveObject, IDisposable, IActivatableViewModel
{
    private bool _disposedValue;
    private ObservableAsPropertyHelper<bool>? _connectivitySubscription;

    protected ViewModelBase(NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IGlobalModalService? globalModalService,
        IConnectivityService? connectivityService = null)
    {
        NetworkProvider = networkProvider;
        LocalizationService = localizationService;
        GlobalModalService = globalModalService!;

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

            _connectivitySubscription = networkStatusStream.ToPropertyEx(this, x => x.IsInNetworkOutage);
        }

        this.WhenActivated(disposables =>
        {
            LanguageChanged
                .Do(_ => this.RaisePropertyChanged(string.Empty))
                .Subscribe()
                .DisposeWith(disposables);
        });
    }

    private IDisposable? _autoRedirectTimer;
    public ILocalizationService LocalizationService { get; }
    protected IGlobalModalService GlobalModalService { get; }
    public ViewModelActivator Activator { get; } = new();

    [ObservableAsProperty] public bool IsInNetworkOutage { get; }

    protected NetworkProvider NetworkProvider { get; }
    protected IObservable<SystemU> LanguageChanged { get; }

    protected const int TOTAL_STEPS = 4;
    protected const int TOTAL_RECOVERY_STEPS = 3;

    protected string StepFormatKey => LocalizationService.GetString(LocalizationKeys.Verification.Info.STEP_OF);

    protected uint ComputeConnectId(PubKeyExchangeType pubKeyExchangeType)
    {
        uint connectId =
            NetworkProvider.ComputeUniqueConnectId(NetworkProvider.ApplicationInstanceSettings,
                pubKeyExchangeType);

        return connectId;
    }

    protected Membership? Membership() =>
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

    protected async Task StartAutoRedirectSequenceAsync(
        IScreen hostScreen,
        string message,
        int seconds,
        Action<IAuthenticationHost> navigationAction,
        string title,
        string subtitle
        )
    {
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;
        });

        if (hostScreen is IAuthenticationHost hostWindow)
        {
            await ShowRedirectNotification(message, seconds, () =>
            {
                Dispatcher.UIThread.Post(async void () =>
                {
                    try
                    {
                        await GlobalModalService.CloseAllAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[VIEWMODEL-BASE] Error closing modals explicitly during redirect sequence");
                    }
                    finally
                    {
                        navigationAction(hostWindow);
                    }
                });
            }, title, subtitle);
        }
    }

    protected async Task ShowRedirectNotification(
        string message,
        int seconds,
        Action onComplete,
        string title,
        string subtitle)
    {
        if (_disposedValue)
        {
            onComplete();
            return;
        }

        RedirectNotificationViewModel redirectViewModel = new(
            title,
            subtitle,
            message,
            seconds,
            onComplete,
            LocalizationService);

        try
        {
            if (!_disposedValue)
            {
                await GlobalModalService.ShowMiddleAsync(redirectViewModel,
                    showScrim: true, isDismissable: false);
            }
            else
            {
                onComplete();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[VIEWMODEL-BASE] Exception showing redirect notification bottom sheet");
            onComplete();
        }
    }

    protected void CleanupAndNavigate(IAuthenticationHost membershipHostWindow, MembershipViewType targetView)
    {
        membershipHostWindow.ClearNavigationStack();
        membershipHostWindow.Navigate.Execute(targetView).Subscribe();

        Task.Run(async () =>
        {
            try
            {
                await GlobalModalService.CloseAllAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[VIEWMODEL-BASE] Exception closing modals during cleanup");
            }
        }).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[VIEWMODEL-BASE] Unhandled exception in CleanupAndNavigate background task");
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
            Debug.WriteLine($"[VIEWMODEL-BASE] CTS already disposed during recreation: {ex.Message}");
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
                Debug.WriteLine($"[VIEWMODEL-BASE] CTS already disposed during recreation (Option): {ex.Message}");
            }
            cts.Dispose();
        });

        CancellationTokenSource newCts = new();
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

            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;
        }

        _disposedValue = true;
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
