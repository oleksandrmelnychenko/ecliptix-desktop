using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
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

public sealed class VerifyOtpViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly ByteString _mobileNumberIdentifier;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly IPasswordRecoveryService? _passwordRecoveryService;
    private readonly ILocalizationService _localizationService;
    private readonly AuthenticationFlowContext _flowContext;
    private readonly Lock _sessionLock = new();
    private readonly CompositeDisposable _disposables = new();

    private Guid _verificationSessionIdentifier = Guid.Empty;
    private IDisposable? _autoRedirectTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private volatile bool _isDisposed;

    public VerifyOtpViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        ByteString mobileNumberIdentifier,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        AuthenticationFlowContext flowContext = AuthenticationFlowContext.Registration,
        IPasswordRecoveryService? passwordRecoveryService = null) : base(networkProvider,
        localizationService)
    {
        _mobileNumberIdentifier = mobileNumberIdentifier;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _registrationService = registrationService;
        _passwordRecoveryService = passwordRecoveryService;
        _flowContext = flowContext;
        _localizationService = localizationService;

        if (flowContext == AuthenticationFlowContext.PasswordRecovery && passwordRecoveryService == null)
        {
            throw new ArgumentNullException(nameof(passwordRecoveryService),
                "Password recovery service is required when flow context is PasswordRecovery");
        }

        HostScreen = hostScreen;

        NavToPasswordConfirmation = ReactiveCommand.CreateFromObservable(() =>
        {
            AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;
            MembershipViewType nextView = _flowContext == AuthenticationFlowContext.Registration
                ? MembershipViewType.ConfirmSecureKey
                : MembershipViewType.ForgotPasswordReset;
            return hostWindow.Navigate.Execute(nextView);
        });

        IObservable<bool> canVerify = this.WhenAnyValue(
            x => x.VerificationCode,
            x => x.RemainingTime,
            (code, time) => code.Length == 6 && code.All(char.IsDigit) &&
                            time != AuthenticationConstants.ExpiredRemainingTime
        );
        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canVerify);

        SendVerificationCodeCommand.ThrownExceptions
            .Subscribe(ex =>
            {
                Log.Error(ex, "[OTP-VERIFICATION] Unhandled exception in SendVerificationCodeCommand");
                if (!_isDisposed)
                {
                    ErrorMessage = ex.Message;
                    IsSent = false;
                    HasError = true;
                }
            })
            .DisposeWith(_disposables);

        IObservable<bool> canResend = this.WhenAnyValue(
                x => x.SecondsRemaining,
                x => x.HasValidSession,
                x => x.CurrentStatus)
            .Select(tuple => tuple is { Item2: true, Item1: 0 } &&
                             tuple.Item3 == VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired)
            .DistinctUntilChanged()
            .Catch<bool, Exception>(ex => Observable.Return(false));
        ResendSendVerificationCodeCommand = ReactiveCommand.CreateFromTask(ReSendVerificationCode, canResend);

        ResendSendVerificationCodeCommand.ThrownExceptions
            .Subscribe(ex =>
            {
                Log.Error(ex, "[OTP-RESEND] Unhandled exception in ResendSendVerificationCodeCommand");
                if (!_isDisposed)
                {
                    ErrorMessage = ex.Message;
                    HasError = true;
                }
            })
            .DisposeWith(_disposables);

        SendVerificationCodeCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        ResendSendVerificationCodeCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsResending)
            .DisposeWith(_disposables);

        this.WhenActivated(disposables =>
        {
            OnViewLoaded().Subscribe().DisposeWith(disposables).DisposeWith(_disposables);

            this.WhenAnyValue(x => x.ErrorMessage)
                .DistinctUntilChanged()
                .Subscribe(err
                    =>
                {
                    HasError = !string.IsNullOrEmpty(err);
                    if (!string.IsNullOrEmpty(err) && HostScreen is AuthenticationViewModel hostWindow)
                        ShowServerErrorNotification(hostWindow, err);
                })
                .DisposeWith(disposables).DisposeWith(_disposables);

            this.WhenAnyValue(x => x.SecondsRemaining)
                .Select(FormatRemainingTime)
                .Subscribe(rt => RemainingTime = rt)
                .DisposeWith(disposables).DisposeWith(_disposables);
        });
    }

    public string? UrlPathSegment { get; } = "/verification-code-entry";
    public IScreen HostScreen { get; }
    public new ViewModelActivator Activator { get; } = new();

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, IRoutableViewModel> NavToPasswordConfirmation { get; }

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
    [Reactive] public bool HasValidSession { get; private set; }

    [ObservableAsProperty] public bool IsBusy { get; }
    [ObservableAsProperty] public bool IsResending { get; }

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

    public async Task HandleEnterKeyPressAsync()
    {
        try
        {
            if (_isDisposed) return;

            if (await SendVerificationCodeCommand.CanExecute.FirstOrDefaultAsync())
            {
                SendVerificationCodeCommand.Execute().Subscribe().DisposeWith(_disposables);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OTP-ENTERKEY] Error handling enter key press");
        }
    }

    public void ResetState()
    {
        if (_isDisposed) return;

        CancellationTokenSource? cts = Interlocked.Exchange(ref _cancellationTokenSource, null);
        cts?.Cancel();
        cts?.Dispose();

        _ = Task.Run(async () =>
        {
            await ResetUiState();
            await CleanupSessionAsync();
        });
    }

    private IObservable<Unit> OnViewLoaded()
    {
        return Observable.FromAsync(async () =>
        {
            if (_isDisposed) return;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            _disposables.Add(_cancellationTokenSource);

            string deviceIdentifier = SystemDeviceIdentifier();

            Task<Result<Ecliptix.Utilities.Unit, string>> initiateTask =
                _flowContext == AuthenticationFlowContext.Registration
                    ? _registrationService.InitiateOtpVerificationAsync(
                        _mobileNumberIdentifier,
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
                        cancellationToken: _cancellationTokenSource.Token)
                    : _passwordRecoveryService!.InitiatePasswordResetOtpAsync(
                        _mobileNumberIdentifier,
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
                        cancellationToken: _cancellationTokenSource.Token);

            Result<Ecliptix.Utilities.Unit, string> result = await initiateTask;

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

        CancellationToken operationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

        Task<Result<Membership, string>> verifyTask = _flowContext == AuthenticationFlowContext.Registration
            ? _registrationService.VerifyOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                systemDeviceIdentifier,
                connectId,
                operationToken)
            : _passwordRecoveryService!.VerifyPasswordResetOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                systemDeviceIdentifier,
                connectId,
                operationToken);

        Result<Membership, string> result = await verifyTask;

        if (_isDisposed) return;

        if (result.IsOk)
        {
            Membership membership = result.Unwrap();

            if (membership.CreationStatus == Protobuf.Membership.Membership.Types.CreationStatus.SecureKeySet &&
                _flowContext == AuthenticationFlowContext.Registration)
            {
                await ShowAccountExistsRedirectAsync();
            }
            else
            {
                if (!_isDisposed && HostScreen is AuthenticationViewModel hostWindow)
                {
                    await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);
                    NavToPasswordConfirmation.Execute().Subscribe().DisposeWith(_disposables);
                    hostWindow.ClearNavigationStack(true);
                }
            }

            if (HasValidSession)
            {
                if (_flowContext == AuthenticationFlowContext.Registration)
                {
                    await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier!.Value)
                        .WaitAsync(AuthenticationConstants.Timeouts.CleanupTimeout, operationToken);
                }
                else if (_passwordRecoveryService != null)
                {
                    await _passwordRecoveryService
                        .CleanupPasswordResetSessionAsync(VerificationSessionIdentifier!.Value)
                        .WaitAsync(AuthenticationConstants.Timeouts.CleanupTimeout, operationToken);
                }
            }
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

    private Task ShowAccountExistsRedirectAsync()
    {
        if (_isDisposed) return Task.CompletedTask;

        string message = LocalizationService[AuthenticationConstants.AccountAlreadyExistsKey];

        if (HostScreen is AuthenticationViewModel hostWindow)
        {
            ShowRedirectNotification(hostWindow, message, 8, () =>
            {
                if (!_isDisposed)
                {
                    CleanupAndNavigate(hostWindow, MembershipViewType.Welcome);
                }
            });
        }

        return Task.CompletedTask;
    }

    private Task ReSendVerificationCode()
    {
        if (_isDisposed) return Task.CompletedTask;

        if (HasValidSession)
        {
            ErrorMessage = string.Empty;
            HasError = false;

            string deviceIdentifier = SystemDeviceIdentifier();

            CancellationTokenSource? oldCts = _cancellationTokenSource;
            _cancellationTokenSource = new CancellationTokenSource();
            _disposables.Add(_cancellationTokenSource);

            oldCts?.Dispose();

            Task.Run(async () =>
            {
                if (_isDisposed || _cancellationTokenSource.Token.IsCancellationRequested)
                    return;

                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                Task<Result<Ecliptix.Utilities.Unit, string>> resendTask =
                    _flowContext == AuthenticationFlowContext.Registration
                        ? _registrationService.ResendOtpVerificationAsync(
                            VerificationSessionIdentifier!.Value,
                            _mobileNumberIdentifier,
                            deviceIdentifier,
                            onCountdownUpdate: (seconds, identifier, status, message) =>
                                RxApp.MainThreadScheduler.Schedule(() =>
                                {
                                    if (!_isDisposed)
                                        HandleCountdownUpdate(seconds, identifier, status, message);
                                }),
                            cancellationToken: _cancellationTokenSource.Token)
                        : _passwordRecoveryService!.ResendPasswordResetOtpAsync(
                            VerificationSessionIdentifier!.Value,
                            _mobileNumberIdentifier,
                            deviceIdentifier,
                            onCountdownUpdate: (seconds, identifier, status, message) =>
                                RxApp.MainThreadScheduler.Schedule(() =>
                                {
                                    if (!_isDisposed)
                                        HandleCountdownUpdate(seconds, identifier, status, message);
                                }),
                            cancellationToken: _cancellationTokenSource.Token);

                Result<Ecliptix.Utilities.Unit, string> result = await resendTask;

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
            }, _cancellationTokenSource.Token);
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
        _ = StartAutoRedirectAsync(5, MembershipViewType.Welcome);
        return 0;
    }

    private uint HandleNotFoundStatus()
    {
        _ = StartAutoRedirectAsync(5, MembershipViewType.Welcome);
        return 0;
    }

    private uint HandleFailedStatus(string? error)
    {
        _ = !string.IsNullOrEmpty(error)
            ? StartAutoRedirectAsync(5, MembershipViewType.Welcome, error)
            : StartAutoRedirectAsync(5, MembershipViewType.Welcome);

        HasError = true;
        HasValidSession = false;
        return 0;
    }

    private uint HandleUnavailable()
    {
        ErrorMessage = string.Empty;
        SecondsRemaining = 0;
        return 0;
    }

    private async Task StartAutoRedirectAsync(int seconds, MembershipViewType targetView, string localizaedMessage = "")
    {
        try
        {
            if (_isDisposed) return;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _autoRedirectTimer?.Dispose();
                _autoRedirectTimer = null;

                AutoRedirectCountdown = seconds;
                IsUiLocked = true;
                return Task.CompletedTask;
            });

            string message;

            if (!string.IsNullOrEmpty(localizaedMessage))
            {
                message = localizaedMessage;
            }
            else
            {
                string key = IsMaxAttemptsReached
                    ? AuthenticationConstants.MaxAttemptsReachedKey
                    : AuthenticationConstants.SessionNotFoundKey;

                message = _localizationService.GetString(key);
            }

            if (HostScreen is AuthenticationViewModel hostWindow)
            {
                ShowRedirectNotification(hostWindow, message, seconds, () =>
                {
                    if (!_isDisposed)
                        _ = CleanupAndNavigateAsync(targetView);
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OTP-AUTO-REDIRECT] Error during auto-redirect");
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
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.ServerUnavailable => HandleUnavailable(),
            _ => Math.Min(seconds, SecondsRemaining)
        };
        CurrentStatus = status;
    }

    private async Task CleanupAndNavigateAsync(MembershipViewType targetView)
    {
        try
        {
            if (_isDisposed) return;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (!_isDisposed && HostScreen is AuthenticationViewModel membershipHostWindow)
                {
                    CleanupAndNavigate(membershipHostWindow, targetView);
                }

                return Task.CompletedTask;
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[OTP-CLEANUP-NAV] Error during cleanup and navigation");
        }
    }

    private static string FormatRemainingTime(uint seconds) =>
        TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");

    private async Task CleanupSessionAsync()
    {
        if (HasValidSession)
        {
            Guid sessionId = VerificationSessionIdentifier!.Value;

            if (_flowContext == AuthenticationFlowContext.Registration)
            {
                await _registrationService.CleanupVerificationSessionAsync(sessionId);
            }
            else if (_passwordRecoveryService != null)
            {
                await _passwordRecoveryService.CleanupPasswordResetSessionAsync(sessionId);
            }
        }
    }

    private async Task ResetUiState()
    {
        if (_isDisposed) return;

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            IsUiLocked = false;

            if (HostScreen is AuthenticationViewModel hostWindow)
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

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_isDisposed)
        {
            _isDisposed = true;
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _autoRedirectTimer?.Dispose();
            _disposables?.Dispose();
        }

        base.Dispose(disposing);
    }
}
