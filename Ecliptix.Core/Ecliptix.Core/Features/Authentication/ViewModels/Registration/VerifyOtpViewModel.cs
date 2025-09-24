using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Protobuf.Protocol;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class VerifyOtpViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly ByteString _phoneNumberIdentifier;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly ILocalizationService _localizationService;
    private readonly IUiDispatcher _uiDispatcher;

    public string? UrlPathSegment { get; } = "/verification-code-entry";
    public IScreen HostScreen { get; }

    private readonly Lock _sessionLock = new();

    private Guid _verificationSessionIdentifier = Guid.Empty;

    private Guid? VerificationSessionIdentifier
    {
        get
        {
            lock (_sessionLock)
            {
                return _verificationSessionIdentifier == Guid.Empty ? null : _verificationSessionIdentifier;
            }
        }
    }

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToPasswordConfirmation { get; }

    public new ViewModelActivator Activator { get; } = new();

    [Reactive] public string VerificationCode { get; set; } = string.Empty;
    [Reactive] public bool IsSent { get; private set; }
    [Reactive] public string ErrorMessage { get; private set; } = string.Empty;
    [Reactive] public string RemainingTime { get; private set; } = AuthenticationConstants.InitialRemainingTime;
    [Reactive] public uint SecondsRemaining { get; private set; }
    [Reactive] public bool HasError { get; private set; }

    [Reactive]
    public VerificationCountdownUpdate.Types.CountdownUpdateStatus CurrentStatus { get; private set; } =
        VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active;

    [Reactive] public bool IsMaxAttemptsReached { get; private set; }
    [Reactive] public int AutoRedirectCountdown { get; private set; }
    [Reactive] public string AutoRedirectMessage { get; private set; } = string.Empty;

    [Reactive] public bool IsUiLocked { get; private set; }
    [Reactive] public bool ShowDimmer { get; private set; }
    [Reactive] public bool ShowSpinner { get; private set; }
    [Reactive] public bool HasValidSession { get; private set; }


    private IDisposable? _autoRedirectTimer;
    private CancellationTokenSource? _operationCts;
    private readonly CompositeDisposable _disposables = new();
    private volatile bool _isDisposed;

    public VerifyOtpViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        ByteString phoneNumberIdentifier,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        IUiDispatcher uiDispatcher) : base(systemEventService, networkProvider,
        localizationService)
    {
        _phoneNumberIdentifier = phoneNumberIdentifier;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _registrationService = registrationService;
        _localizationService = localizationService;
        _uiDispatcher = uiDispatcher;

        HostScreen = hostScreen;

        NavToPasswordConfirmation = ReactiveCommand.CreateFromObservable(() =>
        {
            MembershipHostWindowModel hostWindow = (MembershipHostWindowModel)HostScreen;
            return hostWindow.Navigate.Execute(MembershipViewType.ConfirmSecureKey);
        });

        IObservable<bool> canVerify = this.WhenAnyValue(
            x => x.VerificationCode,
            x => x.RemainingTime,
            (code, time) => code.Length == 6 && code.All(char.IsDigit) &&
                            time != AuthenticationConstants.ExpiredRemainingTime
        );
        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canVerify);

        IObservable<bool> canResend = this.WhenAnyValue(x => x.SecondsRemaining, x => x.HasValidSession)
            .Select(tuple => tuple is { Item1: 0, Item2: true })
            .Catch<bool, Exception>(ex => Observable.Return(false));
        ResendSendVerificationCodeCommand = ReactiveCommand.CreateFromTask(ReSendVerificationCode, canResend);

        this.WhenActivated(disposables =>
        {
            OnViewLoaded().Subscribe().DisposeWith(disposables).DisposeWith(_disposables);

            this.WhenAnyValue(x => x.ErrorMessage)
                .Select(e => !string.IsNullOrEmpty(e))
                .DistinctUntilChanged()
                .Subscribe(flag => HasError = flag)
                .DisposeWith(disposables).DisposeWith(_disposables);

            this.WhenAnyValue(x => x.SecondsRemaining)
                .Select(FormatRemainingTime)
                .Subscribe(rt => RemainingTime = rt)
                .DisposeWith(disposables).DisposeWith(_disposables);
        });
    }

    private IObservable<Unit> OnViewLoaded()
    {
        return Observable.FromAsync(async () =>
        {
            if (_isDisposed) return;

            _operationCts?.Cancel();
            _operationCts?.Dispose();
            _operationCts = new CancellationTokenSource();
            _disposables.Add(_operationCts);

            string deviceIdentifier = SystemDeviceIdentifier();
            Result<Ecliptix.Utilities.Unit, string> result = await _registrationService.InitiateOtpVerificationAsync(
                _phoneNumberIdentifier,
                deviceIdentifier,
                onCountdownUpdate: (seconds, identifier, status, message) =>
                    RxApp.MainThreadScheduler.Schedule(() =>
                    {
                        if (_isDisposed) return;
                        try
                        {
                            HandleCountdownUpdate(seconds, identifier, status, message);
                        }
                        catch (ObjectDisposedException)
                        {
                        }
                    }),
                cancellationToken: _operationCts.Token
            );

            if (result.IsErr && !_isDisposed)
            {
                ErrorMessage = result.UnwrapErr();
            }
        });
    }

    private async Task SendVerificationCode()
    {
        if (_isDisposed) return;

        string systemDeviceIdentifier = SystemDeviceIdentifier();

        IsSent = true;
        ErrorMessage = string.Empty;

        if (!HasValidSession)
        {
            IsSent = false;
            HasError = true;
            ErrorMessage = _localizationService[AuthenticationConstants.NoVerificationSessionKey];
            return;
        }

        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(30));
        using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
            timeoutCts.Token,
            _operationCts?.Token ?? CancellationToken.None);

        Result<Membership, string> result =
            await _registrationService.VerifyOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                systemDeviceIdentifier,
                connectId);

        if (_isDisposed) return;

        if (result.IsOk)
        {
            Membership membership = result.Unwrap();
            await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);

            if (HasValidSession)
            {
                await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier!.Value)
                    .WaitAsync(AuthenticationConstants.Timeouts.CleanupTimeout, combinedCts.Token);
            }

            if (!_isDisposed)
                NavToPasswordConfirmation.Execute().Subscribe().DisposeWith(_disposables);
        }
        else
        {
            if (!_isDisposed)
            {
                ErrorMessage = result.UnwrapErr();
                IsSent = false;
            }
        }
    }

    private Task ReSendVerificationCode()
    {
        if (_isDisposed) return Task.CompletedTask;

        if (HasValidSession)
        {
            ErrorMessage = string.Empty;
            HasError = false;

            string deviceIdentifier = SystemDeviceIdentifier();

            CancellationTokenSource? oldCts = _operationCts;
            _operationCts = new CancellationTokenSource();
            _disposables.Add(_operationCts);

            oldCts?.Dispose();

            Task.Run(async () =>
            {
                try
                {
                    if (_isDisposed || _operationCts.Token.IsCancellationRequested)
                        return;

                    _operationCts.Token.ThrowIfCancellationRequested();

                    Result<Ecliptix.Utilities.Unit, string> result =
                        await _registrationService.ResendOtpVerificationAsync(
                            VerificationSessionIdentifier!.Value,
                            _phoneNumberIdentifier,
                            deviceIdentifier,
                            onCountdownUpdate: (seconds, identifier, status, message) =>
                                RxApp.MainThreadScheduler.Schedule(() =>
                                {
                                    if (!_isDisposed)
                                        HandleCountdownUpdate(seconds, identifier, status, message);
                                }),
                            cancellationToken: _operationCts.Token);

                    if (result.IsErr && !_isDisposed)
                    {
                        RxApp.MainThreadScheduler.Schedule(() =>
                        {
                            if (!_isDisposed)
                            {
                                ErrorMessage = result.UnwrapErr();
                                HasError = true;
                                SecondsRemaining = 0;
                            }
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    if (!_isDisposed)
                        Log.Warning("Resend verification failed: {Error}", ex.Message);
                }
            }, _operationCts.Token);
        }
        else
        {
            SecondsRemaining = 0;
            ErrorMessage = _localizationService[AuthenticationConstants.NoActiveVerificationSessionKey];
            HasError = true;
            HasValidSession = false;
        }

        return Task.CompletedTask;
    }

    private uint HandleMaxAttemptsStatus()
    {
        IsMaxAttemptsReached = true;
        ErrorMessage = _localizationService[AuthenticationConstants.MaxAttemptsReachedKey];
        StartAutoRedirect(5, MembershipViewType.Welcome);
        return 0;
    }

    private uint HandleNotFoundStatus()
    {
        ErrorMessage = _localizationService[AuthenticationConstants.SessionNotFoundKey];
        StartAutoRedirect(5, MembershipViewType.Welcome);
        return 0;
    }

    private uint HandleFailedStatus(string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            ErrorMessage = error;
        }

        HasValidSession = false;
        return 0;
    }

    private async void StartAutoRedirect(int seconds, MembershipViewType targetView)
    {
        if (_isDisposed) return;

        await _uiDispatcher.PostAsync(() =>
        {
            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            AutoRedirectCountdown = seconds;
            IsUiLocked = true;
            ShowDimmer = true;
            ShowSpinner = true;
            return Task.CompletedTask;
        });

        string key = IsMaxAttemptsReached
            ? AuthenticationConstants.MaxAttemptsReachedKey
            : AuthenticationConstants.SessionNotFoundKey;

        string message = _localizationService.GetString(key);

        ShowRedirectNotification(BottomSheetComponentType.RedirectNotification, message, seconds, () =>
        {
            if (!_isDisposed)
                CleanupAndNavigate(targetView);
        });
    }

    private void ShowRedirectNotification(BottomSheetComponentType componentType, string message, int seconds, Action onComplete)
    {
        if (_isDisposed)
        {
            onComplete();
            return;
        }

        RedirectNotificationViewModel redirectViewModel = new(message, seconds, onComplete, _localizationService);
        RedirectNotificationView redirectView = new() { DataContext = redirectViewModel };

        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!_isDisposed)
                        await hostWindow.ShowBottomSheet(componentType, redirectView, showScrim: true, isDismissable: false);
                    else
                        onComplete();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to show redirect notification");
                    onComplete();
                }
            });
        }
        else
        {
            onComplete();
        }
    }

    private void HandleCountdownUpdate(uint seconds, Guid identifier,
        VerificationCountdownUpdate.Types.CountdownUpdateStatus status, string? message)
    {
        if (_isDisposed) return;

        if (identifier != Guid.Empty)
        {
            lock (_sessionLock)
            {
                if (_isDisposed) return;

                if (_verificationSessionIdentifier == Guid.Empty)
                {
                    _verificationSessionIdentifier = identifier;
                    HasValidSession = true;
                }
                else if (_verificationSessionIdentifier != identifier)
                {
                    Log.Warning("Attempted to overwrite session identifier {Previous} with {New}",
                        _verificationSessionIdentifier, identifier);
                    return;
                }
            }
        }

        if (_isDisposed) return;

        SecondsRemaining = status switch
        {
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active => seconds,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired => 0,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed => HandleFailedStatus(message),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound => HandleNotFoundStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached => HandleMaxAttemptsStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.SessionExpired => HandleNotFoundStatus(),
            _ => Math.Min(seconds, SecondsRemaining)
        };
        CurrentStatus = status;
    }

    private async void CleanupAndNavigate(MembershipViewType targetView)
    {
        if (_isDisposed) return;

        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = null;

        await _uiDispatcher.PostAsync(() =>
        {
            if (!_isDisposed && HostScreen is MembershipHostWindowModel membershipHostWindow)
            {
                membershipHostWindow.Navigate.Execute(targetView).Subscribe();
                membershipHostWindow.ClearNavigationStack();
            }

            return Task.CompletedTask;
        });

        _ = Task.Run(async () =>
        {
            try
            {
                if (HostScreen is MembershipHostWindowModel hostWindow)
                {
                    await hostWindow.HideBottomSheetAsync();
                }
            }
            catch (Exception ex)
            {
                Log.Warning("Background cleanup failed: {Error}", ex.Message);
            }
        });
    }

    private static string FormatRemainingTime(uint seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string formattedDataTimeString = time.ToString(@"mm\:ss");
        return formattedDataTimeString;
    }

    public async void HandleEnterKeyPress()
    {
        if (_isDisposed) return;

        if (await SendVerificationCodeCommand.CanExecute.FirstOrDefaultAsync())
        {
            SendVerificationCodeCommand.Execute().Subscribe().DisposeWith(_disposables);
        }
    }

    private async Task CleanupSessionAsync()
    {
        if (HasValidSession)
        {
            try
            {
                Guid sessionId = VerificationSessionIdentifier!.Value;
                await _registrationService.CleanupVerificationSessionAsync(sessionId);
            }
            catch (Exception ex)
            {
                Log.Warning("Session cleanup failed (non-critical): {Error}", ex.Message);
            }
        }
    }

    private async Task ResetUiState()
    {
        if (_isDisposed) return;

        await _uiDispatcher.PostAsync(async () =>
        {
            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            IsUiLocked = false;
            ShowDimmer = false;
            ShowSpinner = false;

            if (HostScreen is MembershipHostWindowModel hostWindow)
            {
                await hostWindow.HideBottomSheetAsync();
            }

            VerificationCode = "";
            IsSent = false;
            ErrorMessage = string.Empty;
            HasError = false;
            SecondsRemaining = 0;
            RemainingTime = AuthenticationConstants.InitialRemainingTime;
            IsMaxAttemptsReached = false;
            AutoRedirectMessage = string.Empty;
            AutoRedirectCountdown = 0;
            CurrentStatus = VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active;
            lock (_sessionLock)
            {
                _verificationSessionIdentifier = Guid.Empty;
                HasValidSession = false;
            }
        });
    }

    public void ResetState()
    {
        if (_isDisposed) return;

        _operationCts?.Cancel();

        _ = Task.Run(async () =>
        {
            try
            {
                await ResetUiState();
                await CleanupSessionAsync();
            }
            catch (Exception ex)
            {
                Log.Warning("ResetState cleanup failed: {Error}", ex.Message);
            }
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isDisposed)
        {
            _isDisposed = true;

            try
            {
                _operationCts?.Cancel();
                _operationCts?.Dispose();
                _autoRedirectTimer?.Dispose();
                _disposables?.Dispose();
            }
            catch (Exception ex)
            {
                Log.Warning("Dispose cleanup failed: {Error}", ex.Message);
            }
        }

        base.Dispose(disposing);
    }
}