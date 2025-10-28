using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
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
    private IDisposable? _cooldownTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private volatile bool _isDisposed;

    public VerifyOtpViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        ByteString mobileNumberIdentifier,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        AuthenticationFlowContext flowContext = AuthenticationFlowContext.Registration,
        IPasswordRecoveryService? passwordRecoveryService = null) : base(networkProvider,
        localizationService, connectivityService)
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

        Log.Information("[VERIFYOTP-VM] Initialized with flow context: {FlowContext}", flowContext);

        HostScreen = hostScreen;

        NavToPasswordConfirmation = ReactiveCommand.CreateFromObservable(() =>
        {
            AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;
            return hostWindow.Navigate.Execute(MembershipViewType.ConfirmSecureKey);
        });

        IObservable<bool> canVerify = this.WhenAnyValue(
            x => x.VerificationCode,
            x => x.RemainingTime,
            x => x.IsInNetworkOutage,
            (code, time, isInOutage) => code.Length == 6 && code.All(char.IsDigit) &&
                            time != AuthenticationConstants.ExpiredRemainingTime && !isInOutage
        );
        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canVerify);

        SendVerificationCodeCommand.ThrownExceptions
            .Subscribe(ex =>
            {
                Log.Error(ex, "[OTP-VERIFICATION] Unhandled exception in SendVerificationCodeCommand");
                if (_isDisposed)
                {
                    return;
                }

                ErrorMessage = ex.Message;
                IsSent = false;
                HasError = true;
            })
            .DisposeWith(_disposables);

        IObservable<bool> canResend = this.WhenAnyValue(
                x => x.SecondsRemaining,
                x => x.HasValidSession,
                x => x.CurrentStatus,
                x => x.CooldownBufferSeconds,
                x => x.IsInNetworkOutage)
            .Select(tuple =>
            {
                if (!tuple.Item2 || tuple.Item5)
                {
                    return false;
                }

                if (tuple.Item3 == VerificationCountdownUpdate.Types.CountdownUpdateStatus.ResendCooldown)
                {
                    return tuple.Item4 == 0;
                }

                if (tuple.Item1 != 0)
                {
                    return false;
                }

                return tuple.Item3 == VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired;
            })
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
                    {
                        ShowServerErrorNotification(hostWindow, err);
                    }
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

    [Reactive] public uint CooldownBufferSeconds { get; private set; }

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
            if (_isDisposed)
            {
                return;
            }

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
        if (_isDisposed)
        {
            return;
        }

        CancellationTokenSource? cts = Interlocked.Exchange(ref _cancellationTokenSource, null);
        cts?.Cancel();
        cts?.Dispose();

        _ = Task.Run(async () =>
        {
            await ResetUiState();
            await CleanupSessionAsync();
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
            _cooldownTimer?.Dispose();
            _disposables?.Dispose();
        }

        base.Dispose(disposing);
    }

    private IObservable<Unit> OnViewLoaded()
    {
        return Observable.FromAsync(async () =>
        {
            if (_isDisposed)
            {
                return;
            }

            CancellationTokenSource cancellationTokenSource = RecreateCancellationToken(ref _cancellationTokenSource);
            _disposables.Add(cancellationTokenSource);

            string deviceIdentifier = SystemDeviceIdentifier();

            Task<Result<Ecliptix.Utilities.Unit, string>> initiateTask =
                _flowContext == AuthenticationFlowContext.Registration
                    ? _registrationService.InitiateOtpVerificationAsync(
                        _mobileNumberIdentifier,
                        deviceIdentifier,
                        onCountdownUpdate: (seconds, identifier, status, message) =>
                            RxApp.MainThreadScheduler.Schedule(() =>
                            {
                                if (_isDisposed)
                                {
                                    return;
                                }

                                try
                                {
                                    HandleCountdownUpdate(seconds, identifier, status, message);
                                }
                                catch (ObjectDisposedException)
                                {
                                }
                            }),
                        cancellationToken: _cancellationTokenSource != null ? _cancellationTokenSource.Token : CancellationToken.None)
                    : _passwordRecoveryService!.InitiatePasswordResetOtpAsync(
                        _mobileNumberIdentifier,
                        deviceIdentifier,
                        onCountdownUpdate: (seconds, identifier, status, message) =>
                            RxApp.MainThreadScheduler.Schedule(() =>
                            {
                                if (_isDisposed)
                                {
                                    return;
                                }

                                try
                                {
                                    HandleCountdownUpdate(seconds, identifier, status, message);
                                }
                                catch (ObjectDisposedException)
                                {
                                }
                            }),
                        cancellationToken: _cancellationTokenSource?.Token ?? CancellationToken.None);

            Result<Ecliptix.Utilities.Unit, string> result = await initiateTask;

            if (result.IsErr && !_isDisposed)
            {
                if (CurrentStatus != VerificationCountdownUpdate.Types.CountdownUpdateStatus.ServerUnavailable)
                {
                    ErrorMessage = result.UnwrapErr();
                }
            }
        });
    }

    private async Task SendVerificationCode()
    {
        if (_isDisposed)
        {
            return;
        }

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

        if (_isDisposed)
        {
            return;
        }

        if (result.IsOk)
        {
            Membership membership = result.Unwrap();

            if (!_isDisposed && HostScreen is AuthenticationViewModel hostWindow)
            {
                await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);

                if (membership.AccountUniqueIdentifier != null && membership.AccountUniqueIdentifier.Length > 0)
                {
                    await _applicationSecureStorageProvider
                        .SetCurrentAccountIdAsync(membership.AccountUniqueIdentifier)
                        .ConfigureAwait(false);
                    Log.Information(
                        "[OTP-VERIFY-ACCOUNT] Active account stored from OTP verification. MembershipId: {MembershipId}, AccountId: {AccountId}",
                        Helpers.FromByteStringToGuid(membership.UniqueIdentifier),
                        Helpers.FromByteStringToGuid(membership.AccountUniqueIdentifier));
                }

                NavToPasswordConfirmation.Execute().Subscribe().DisposeWith(_disposables);
                hostWindow.ClearNavigationStack(true);
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

    private Task ReSendVerificationCode()
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }

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
                {
                    return;
                }

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
                                    {
                                        HandleCountdownUpdate(seconds, identifier, status, message);
                                    }
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
                                    {
                                        HandleCountdownUpdate(seconds, identifier, status, message);
                                    }
                                }),
                            cancellationToken: _cancellationTokenSource.Token);

                Result<Ecliptix.Utilities.Unit, string> result = await resendTask;

                if (result.IsErr && !_isDisposed)
                {
                    RxApp.MainThreadScheduler.Schedule(() =>
                    {
                        if (!_isDisposed)
                        {
                            string error = result.UnwrapErr();

                            if (IsServerUnavailableError(error))
                            {
                                ErrorMessage = error;
                                _ = StartAutoRedirectAsync(5, MembershipViewType.Welcome, error);
                                HasError = true;
                                HasValidSession = false;
                            }
                            else
                            {
                                ErrorMessage = error;
                                HasError = true;
                                SecondsRemaining = 0;
                            }
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

    private uint HandleResendCooldown(string? message, uint seconds)
    {
        if (!string.IsNullOrEmpty(message))
        {
            string messageWithSeconds = seconds > 0
                ? $"{message}. {seconds} second{(seconds > 1 ? "s" : "")} remaining"
                : message;

            ErrorMessage = messageWithSeconds;
            HasError = true;
        }

        CooldownBufferSeconds = seconds;

        _cooldownTimer?.Dispose();

        if (seconds > 0)
        {
            _cooldownTimer = Observable.Interval(TimeSpan.FromSeconds(1))
                .TakeWhile(_ => CooldownBufferSeconds > 0 && !_isDisposed)
                .ObserveOn(RxApp.MainThreadScheduler)
                .Subscribe(_ =>
                {
                    if (CooldownBufferSeconds > 0)
                    {
                        CooldownBufferSeconds--;
                    }

                    if (CooldownBufferSeconds == 0)
                    {
                        ErrorMessage = string.Empty;
                        HasError = false;
                        CurrentStatus = VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired;
                        _cooldownTimer?.Dispose();
                        _cooldownTimer = null;
                    }
                });
        }

        return 0;
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

    private uint HandleUnavailable(string? message)
    {
        string errorMessage = !string.IsNullOrEmpty(message)
            ? message
            : _localizationService["error.server_unavailable"];

        _ = StartAutoRedirectAsync(5, MembershipViewType.Welcome, errorMessage);
        HasError = true;
        HasValidSession = false;
        return 0;
    }

    private async Task StartAutoRedirectAsync(int seconds, MembershipViewType targetView, string localizaedMessage = "")
    {
        try
        {
            if (_isDisposed)
            {
                return;
            }

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
                    {
                        _ = CleanupAndNavigateAsync(targetView);
                    }
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
        if (_isDisposed)
        {
            return;
        }

        if (identifier != Guid.Empty)
        {
            lock (_sessionLock)
            {
                if (_isDisposed)
                {
                    return;
                }

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

        if (_isDisposed)
        {
            return;
        }

        SecondsRemaining = status switch
        {
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active => seconds,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired => 0,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.ResendCooldown => HandleResendCooldown(message, seconds),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed => HandleFailedStatus(message),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound => HandleNotFoundStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached => HandleMaxAttemptsStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.SessionExpired => HandleNotFoundStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.ServerUnavailable => HandleUnavailable(message),
            _ => Math.Min(seconds, SecondsRemaining)
        };
        CurrentStatus = status;
    }

    private async Task CleanupAndNavigateAsync(MembershipViewType targetView)
    {
        try
        {
            if (_isDisposed)
            {
                return;
            }

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

    private bool IsServerUnavailableError(string errorMessage)
    {
        string serverUnavailableText = _localizationService["error.server_unavailable"];
        string serviceUnavailableText = _localizationService[ErrorI18nKeys.ServiceUnavailable];

        return errorMessage.Contains(serverUnavailableText, StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains(serviceUnavailableText, StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("not responding", StringComparison.OrdinalIgnoreCase);
    }

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
        if (_isDisposed)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            _cooldownTimer?.Dispose();
            _cooldownTimer = null;

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
}
