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
using Ecliptix.Core.Services.Core.Localization;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public sealed partial class VerifyOtpViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly ByteString _mobileNumberIdentifier;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly ISecureKeyRecoveryService? _secureKeyRecoveryService;
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
        AuthenticationFlowContext flowContext = AuthenticationFlowContext.REGISTRATION,
        ISecureKeyRecoveryService? secureKeyRecoveryService = null) : base(networkProvider,
        localizationService, connectivityService)
    {
        _mobileNumberIdentifier = mobileNumberIdentifier;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _registrationService = registrationService;
        _secureKeyRecoveryService = secureKeyRecoveryService;
        _flowContext = flowContext;
        _localizationService = localizationService;

        HostScreen = hostScreen;

        NavToSecureKeyConfirmation = ReactiveCommand.CreateFromObservable(() =>
        {
            AuthenticationViewModel hostWindow = (AuthenticationViewModel)HostScreen;
            return hostWindow.Navigate.Execute(MembershipViewType.SECURE_KEY_CONFIRMATION_VIEW);
        });

        IObservable<bool> canVerify = this.WhenAnyValue(
            x => x.VerificationCode,
            x => x.RemainingTime,
            x => x.IsInNetworkOutage,
            (code, time, isInOutage) => code.Length == 6 && code.All(char.IsDigit) &&
                                        time != AuthenticationConstants.EXPIRED_REMAINING_TIME && !isInOutage
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
                HasError = true;
            })
            .DisposeWith(_disposables);

        IObservable<bool> canResend = this.WhenAnyValue(
                x => x.SecondsRemaining,
                x => x.HasValidSession,
                x => x.CurrentStatus,
                x => x.CooldownBufferSeconds,
                x => x.IsInNetworkOutage)
            .Select(tuple => CanResendVerification(tuple.Item1, tuple.Item2, tuple.Item3, tuple.Item4, tuple.Item5))
            .DistinctUntilChanged()
            .Catch<bool, Exception>(_ => Observable.Return(false));
        ResendSendVerificationCodeCommand = ReactiveCommand.CreateFromTask(ReSendVerificationCode, canResend);

        ResendSendVerificationCodeCommand.ThrownExceptions
            .Subscribe(ex =>
            {
                if (_isDisposed)
                {
                    return;
                }

                ErrorMessage = ex.Message;
                HasError = true;
            })
            .DisposeWith(_disposables);

        SendVerificationCodeCommand.IsExecuting
            .ToProperty(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        ResendSendVerificationCodeCommand.IsExecuting
            .ToProperty(this, x => x.IsResending)
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

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }

    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToSecureKeyConfirmation { get; }

    [Reactive] public string VerificationCode { get; set; } = string.Empty;

    [Reactive] public string ErrorMessage { get; private set; } = string.Empty;

    [Reactive] public string RemainingTime { get; private set; } = AuthenticationConstants.INITIAL_REMAINING_TIME;

    [Reactive] private uint SecondsRemaining { get; set; }

    [Reactive] public bool HasError { get; private set; }

    [Reactive] private uint CooldownBufferSeconds { get; set; }

    [Reactive]
    private VerificationCountdownUpdate.Types.CountdownUpdateStatus CurrentStatus { get; set; } =
        VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active;

    [Reactive] private bool IsMaxAttemptsReached { get; set; }

    [Reactive] private bool HasValidSession { get; set; }

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
        if (_isDisposed)
        {
            return;
        }

        if (await SendVerificationCodeCommand.CanExecute.FirstOrDefaultAsync())
        {
            SendVerificationCodeCommand.Execute().Subscribe().DisposeWith(_disposables);
        }
    }

    public void ResetState()
    {
        if (_isDisposed)
        {
            return;
        }

        CancellationTokenSource? cancellationTokenSource = Interlocked.Exchange(ref _cancellationTokenSource, null);
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();

        Task.Run(async () =>
        {
            await ResetUiState();
            await CleanupSessionAsync();
        }).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[VERIFY-OTP] Unhandled exception in cleanup background task");
                }
            },
            TaskScheduler.Default);
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
            _disposables.Dispose();
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

            Task<Result<Ecliptix.Utilities.Unit, string>> initiateTask = CreateInitiateTask();
            Result<Ecliptix.Utilities.Unit, string> result = await initiateTask;

            HandleInitiateResult(result);
        });
    }

    private Task<Result<Ecliptix.Utilities.Unit, string>> CreateInitiateTask()
    {
        CancellationToken cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?> callback =
            CreateCountdownCallback();

        return _flowContext == AuthenticationFlowContext.REGISTRATION
            ? _registrationService.InitiateOtpVerificationAsync(_mobileNumberIdentifier,
                VerificationPurpose.Registration, callback, cancellationToken)
            : _secureKeyRecoveryService!.InitiateSecureKeyResetOtpAsync(_mobileNumberIdentifier, callback,
                cancellationToken);
    }

    private Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>
        CreateCountdownCallback()
    {
        return (seconds, identifier, status, message) =>
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
                    // Intentionally suppressed: ViewModel disposed during countdown callback
                }
            });
    }

    private void HandleInitiateResult(Result<Ecliptix.Utilities.Unit, string> result)
    {
        bool shouldSetError = result.IsErr
                              && !_isDisposed
                              && CurrentStatus != VerificationCountdownUpdate.Types.CountdownUpdateStatus
                                  .ServerUnavailable;

        if (shouldSetError)
        {
            ErrorMessage = result.UnwrapErr();
        }
    }

    private async Task SendVerificationCode()
    {
        if (_isDisposed)
        {
            return;
        }

        ErrorMessage = string.Empty;

        if (!HasValidSession)
        {
            HandleNoValidSession();
            return;
        }

        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
        CancellationToken operationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;

        Task<Result<Membership, string>> verifyTask = CreateVerifyTask(connectId, operationToken);
        Result<Membership, string> result = await verifyTask;

        if (_isDisposed)
        {
            return;
        }

        if (result.IsOk)
        {
            await HandleSuccessfulVerification(result.Unwrap(), operationToken);
        }
        else
        {
            HandleVerificationError(result.UnwrapErr());
        }
    }

    private void HandleNoValidSession()
    {
        HasError = true;
        ErrorMessage = _localizationService[AuthenticationConstants.NO_VERIFICATION_SESSION_KEY];
    }

    private Task<Result<Membership, string>> CreateVerifyTask(uint connectId, CancellationToken cancellationToken)
    {
        return _flowContext == AuthenticationFlowContext.REGISTRATION
            ? _registrationService.VerifyOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                connectId,
                cancellationToken)
            : _secureKeyRecoveryService!.VerifySecureKeyResetOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                connectId,
                cancellationToken);
    }

    private async Task HandleSuccessfulVerification(Membership membership, CancellationToken operationToken)
    {
        if (HostScreen is AuthenticationViewModel hostWindow)
        {
            await StoreMembershipData(membership);
            NavigateToNextStep(hostWindow);
        }

        if (HasValidSession)
        {
            await CleanupVerificationSession(operationToken);
        }
    }

    private async Task StoreMembershipData(Membership membership)
    {
        await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);

        if (membership.AccountUniqueIdentifier != null && membership.AccountUniqueIdentifier.Length > 0)
        {
            await _applicationSecureStorageProvider
                .SetCurrentAccountIdAsync(membership.AccountUniqueIdentifier)
                .ConfigureAwait(false);
        }
    }

    private void NavigateToNextStep(AuthenticationViewModel hostWindow)
    {
        hostWindow.ClearNavigationStack(true, MembershipViewType.MOBILE_VERIFICATION_VIEW);
        NavToSecureKeyConfirmation.Execute().Subscribe().DisposeWith(_disposables);
    }

    private async Task CleanupVerificationSession(CancellationToken cancellationToken)
    {
        if (_flowContext == AuthenticationFlowContext.REGISTRATION)
        {
            await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier!.Value)
                .WaitAsync(AuthenticationConstants.Timeouts.CleanupTimeout, cancellationToken);
        }
        else if (_secureKeyRecoveryService != null)
        {
            await _secureKeyRecoveryService
                .CleanupSecureKeyResetSessionAsync(VerificationSessionIdentifier!.Value)
                .WaitAsync(AuthenticationConstants.Timeouts.CleanupTimeout, cancellationToken);
        }
    }

    private void HandleVerificationError(string error) => ErrorMessage = error;

    private Task ReSendVerificationCode()
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }

        if (HasValidSession)
        {
            ExecuteResendOperation();
        }
        else
        {
            HandleNoActiveSession();
        }

        return Task.CompletedTask;
    }

    private void ExecuteResendOperation()
    {
        ErrorMessage = string.Empty;
        HasError = false;

        CancellationTokenSource cancellationToken = CreateNewCancellationToken();

        Task.Run(async () =>
        {
            if (_isDisposed || cancellationToken.Token.IsCancellationRequested)
            {
                return;
            }

            cancellationToken.Token.ThrowIfCancellationRequested();

            Task<Result<Ecliptix.Utilities.Unit, string>> resendTask = CreateResendTask(cancellationToken.Token);
            Result<Ecliptix.Utilities.Unit, string> result = await resendTask;

            if (result.IsErr && !_isDisposed)
            {
                HandleResendError(result.UnwrapErr());
            }
        }, cancellationToken.Token);
    }

    private CancellationTokenSource CreateNewCancellationToken()
    {
        CancellationTokenSource? oldCts = _cancellationTokenSource;
        CancellationTokenSource newCts = new();
        _cancellationTokenSource = newCts;
        _disposables.Add(newCts);
        oldCts?.Dispose();
        return newCts;
    }

    private Task<Result<Ecliptix.Utilities.Unit, string>> CreateResendTask(CancellationToken cancellationToken)
    {
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?> countdownCallback =
            (seconds, identifier, status, message) =>
                RxApp.MainThreadScheduler.Schedule(() =>
                {
                    if (!_isDisposed)
                    {
                        HandleCountdownUpdate(seconds, identifier, status, message);
                    }
                });

        return _flowContext == AuthenticationFlowContext.REGISTRATION
            ? _registrationService.ResendOtpVerificationAsync(
                VerificationSessionIdentifier!.Value,
                _mobileNumberIdentifier,
                onCountdownUpdate: countdownCallback,
                cancellationToken: cancellationToken)
            : _secureKeyRecoveryService!.ResendSecureKeyResetOtpAsync(
                VerificationSessionIdentifier!.Value,
                _mobileNumberIdentifier,
                onCountdownUpdate: countdownCallback,
                cancellationToken: cancellationToken);
    }

    private void HandleResendError(string error)
    {
        RxApp.MainThreadScheduler.Schedule(() =>
        {
            if (_isDisposed)
            {
                return;
            }

            if (IsServerUnavailableError(error))
            {
                ErrorMessage = error;
                StartAutoRedirectAsync(5, MembershipViewType.WELCOME_VIEW, error).ContinueWith(
                    task =>
                    {
                        if (task is { IsFaulted: true, Exception: not null })
                        {
                            Log.Error(task.Exception, "[VERIFY-OTP] Unhandled exception in auto-redirect");
                        }
                    },
                    TaskScheduler.Default);
                HasError = true;
                HasValidSession = false;
            }
            else
            {
                ErrorMessage = error;
                HasError = true;
                SecondsRemaining = 0;
            }
        });
    }

    private void HandleNoActiveSession()
    {
        SecondsRemaining = 0;
        ErrorMessage = _localizationService[AuthenticationConstants.NO_ACTIVE_VERIFICATION_SESSION_KEY];
        HasError = true;
        HasValidSession = false;
    }

    private uint HandleResendCooldown(string? message, uint seconds)
    {
        if (!string.IsNullOrEmpty(message))
        {
            string messageWithSeconds;
            if (seconds > 0)
            {
                string pluralSuffix = seconds > 1 ? "s" : "";
                messageWithSeconds = $"{message}. {seconds} second{pluralSuffix} remaining";
            }
            else
            {
                messageWithSeconds = message;
            }

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

                    if (CooldownBufferSeconds != 0)
                    {
                        return;
                    }

                    ErrorMessage = string.Empty;
                    HasError = false;
                    CurrentStatus = VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired;
                    _cooldownTimer?.Dispose();
                    _cooldownTimer = null;
                });
        }

        return 0;
    }

    private uint HandleMaxAttemptsStatus()
    {
        IsMaxAttemptsReached = true;
        StartAutoRedirectAsync(5, MembershipViewType.WELCOME_VIEW).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[VERIFY-OTP] Unhandled exception in max attempts auto-redirect");
                }
            },
            TaskScheduler.Default);
        return 0;
    }

    private uint HandleNotFoundStatus()
    {
        string message = _localizationService[AuthenticationConstants.SESSION_NOT_FOUND_KEY];
        StartAutoRedirectAsync(5, MembershipViewType.WELCOME_VIEW, message).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[VERIFY-OTP] Unhandled exception in not found auto-redirect");
                }
            },
            TaskScheduler.Default);
        return 0;
    }

    private uint HandleSessionExpiredStatus()
    {
        string message = _localizationService[AuthenticationConstants.VERIFICATION_SESSION_EXPIRED_KEY];
        StartAutoRedirectAsync(5, MembershipViewType.WELCOME_VIEW, message).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception,
                        "[VERIFY-OTP] Unhandled exception in session expired auto-redirect");
                }
            },
            TaskScheduler.Default);
        return 0;
    }

    private uint HandleFailedStatus(string? error)
    {
        Task redirectTask = !string.IsNullOrEmpty(error)
            ? StartAutoRedirectAsync(5, MembershipViewType.WELCOME_VIEW, error)
            : StartAutoRedirectAsync(5, MembershipViewType.WELCOME_VIEW);

        redirectTask.ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception,
                        "[VERIFY-OTP] Unhandled exception in failed status auto-redirect");
                }
            },
            TaskScheduler.Default);

        HasError = true;
        HasValidSession = false;
        return 0;
    }

    private uint HandleUnavailable(string? message)
    {
        string errorMessage = !string.IsNullOrEmpty(message)
            ? message
            : _localizationService[LocalizationKeys.Common.SERVER_UNAVAILABLE];

        StartAutoRedirectAsync(5, MembershipViewType.WELCOME_VIEW, errorMessage).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[VERIFY-OTP] Unhandled exception in StartAutoRedirectAsync");
                }
            },
            TaskScheduler.Default);

        HasError = true;
        HasValidSession = false;

        Task.Run(async () =>
        {
            try
            {
                await EnsureProtocolInBackground();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[VERIFY-OTP] Background secrecy channel establishment failed");
            }
        }).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[VERIFY-OTP] Unhandled exception in EnsureProtocolInBackground");
                }
            },
            TaskScheduler.Default);

        return 0;
    }

    private async Task EnsureProtocolInBackground()
    {
        await NetworkProvider.EnsureProtocolForTypeAsync(
            PubKeyExchangeType.DataCenterEphemeralConnect);
    }

    private async Task StartAutoRedirectAsync(int seconds, MembershipViewType targetView, string localizedMessage = "")
    {
        if (_isDisposed)
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            return Task.CompletedTask;
        });

        string message;

        if (!string.IsNullOrEmpty(localizedMessage))
        {
            message = localizedMessage;
        }
        else
        {
            string key = IsMaxAttemptsReached
                ? AuthenticationConstants.MAX_ATTEMPTS_REACHED_KEY
                : AuthenticationConstants.SESSION_NOT_FOUND_KEY;

            message = _localizationService.GetString(key);
        }

        if (HostScreen is AuthenticationViewModel hostWindow)
        {
            ShowRedirectNotification(hostWindow, message, seconds, () =>
            {
                if (!_isDisposed)
                {
                    CleanupAndNavigateAsync(targetView).ContinueWith(
                        task =>
                        {
                            if (task is { IsFaulted: true, Exception: not null })
                            {
                                Log.Error(task.Exception,
                                    "[VERIFY-OTP] Unhandled exception in cleanup and navigate");
                            }
                        },
                        TaskScheduler.Default);
                }
            });
        }
    }

    private void HandleCountdownUpdate(uint seconds, Guid identifier,
        VerificationCountdownUpdate.Types.CountdownUpdateStatus status, string? message)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!ValidateAndUpdateSessionIdentifier(identifier))
        {
            return;
        }

        SecondsRemaining = ProcessCountdownStatus(status, seconds, message);
        CurrentStatus = status;
    }

    private uint ProcessCountdownStatus(
        VerificationCountdownUpdate.Types.CountdownUpdateStatus status,
        uint seconds,
        string? message)
    {
        return status switch
        {
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active => seconds,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired => 0,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.ResendCooldown => HandleResendCooldown(message,
                seconds),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed => HandleFailedStatus(message),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound => HandleNotFoundStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached => HandleMaxAttemptsStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.SessionExpired => HandleSessionExpiredStatus(),
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.ServerUnavailable => HandleUnavailable(message),
            _ => Math.Min(seconds, SecondsRemaining)
        };
    }

    private bool ValidateAndUpdateSessionIdentifier(Guid identifier)
    {
        if (identifier == Guid.Empty)
        {
            return true;
        }

        lock (_sessionLock)
        {
            if (_isDisposed)
            {
                return false;
            }

            if (_verificationSessionIdentifier != Guid.Empty)
            {
                return _verificationSessionIdentifier == identifier;
            }

            _verificationSessionIdentifier = identifier;
            HasValidSession = true;
            return true;
        }
    }

    private async Task CleanupAndNavigateAsync(MembershipViewType targetView)
    {
        if (_isDisposed)
        {
            return;
        }

        if (_cancellationTokenSource != null)
        {
            await _cancellationTokenSource.CancelAsync();
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (!_isDisposed && HostScreen is AuthenticationViewModel membershipHostWindow)
            {
                CleanupAndNavigate(membershipHostWindow, targetView);
            }

            return Task.CompletedTask;
        });
    }

    private static string FormatRemainingTime(uint seconds) => TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");

    private bool IsServerUnavailableError(string errorMessage)
    {
        string serverUnavailableText = _localizationService[LocalizationKeys.Common.SERVER_UNAVAILABLE];
        string serviceUnavailableText = _localizationService[ErrorI18NKeys.SERVICE_UNAVAILABLE];

        //TODO:check
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

            if (_flowContext == AuthenticationFlowContext.REGISTRATION)
            {
                await _registrationService.CleanupVerificationSessionAsync(sessionId);
            }
            else if (_secureKeyRecoveryService != null)
            {
                await _secureKeyRecoveryService.CleanupSecureKeyResetSessionAsync(sessionId);
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

            if (HostScreen is AuthenticationViewModel hostWindow)
            {
                await hostWindow.HideBottomSheetAsync();
            }

            VerificationCode = string.Empty;
            ErrorMessage = string.Empty;
            HasError = false;
            SecondsRemaining = 0;
            RemainingTime = AuthenticationConstants.INITIAL_REMAINING_TIME;
            IsMaxAttemptsReached = false;
            CurrentStatus = VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active;
            lock (_sessionLock)
            {
                _verificationSessionIdentifier = Guid.Empty;
                HasValidSession = false;
            }
        });
    }

    private static bool CanResendVerification(
        uint secondsRemaining,
        bool hasValidSession,
        VerificationCountdownUpdate.Types.CountdownUpdateStatus status,
        uint cooldownBufferSeconds,
        bool isInNetworkOutage)
    {
        if (!hasValidSession || isInNetworkOutage)
        {
            return false;
        }

        if (status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.ResendCooldown)
        {
            return cooldownBufferSeconds == 0;
        }

        if (secondsRemaining != 0)
        {
            return false;
        }

        return status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired;
    }
}
