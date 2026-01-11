using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Services.Localization;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.Services.Authentication.Constants;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Membership;
using MembershipProto = Ecliptix.Protobuf.Membership.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;
using MembershipViewType = Ecliptix.Core.Modularity.Authentication.MembershipViewType;
using Unit = System.Reactive.Unit;
using CountdownUpdateStatus = Ecliptix.Protobuf.Membership.VerificationCountdownUpdate.Types.CountdownUpdateStatus;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration;

public sealed partial class VerificationCodeEntryViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly ByteString _mobileNumberIdentifier;
    private readonly string _mobileNumber;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IAuthRepository _authRepository;
    private readonly ILocalizationService _localizationService;
    private readonly AuthenticationFlowContext _flowContext;
    private readonly Lock _sessionLock = new();
    private readonly CompositeDisposable _disposables = new();

    private Guid _verificationSessionIdentifier = Guid.Empty;
    private IDisposable? _cooldownTimer;
    private CancellationTokenSource? _cancellationTokenSource;
    private volatile bool _isDisposed;
    private long _countdownVersion;
    private int _alreadyVerifiedHandled;
    private long _autoRedirectVersion;

    private uint? _initialTotalSeconds;
    private readonly Subject<string> _executionErrorSubject = new();
    private readonly IGlobalModalService _globalModalService;
    public IObservable<string> ExecutionError => _executionErrorSubject.AsObservable();

    [Reactive] public double ProgressValue { get; private set; } = 1.0;
    private const int CURRENT_STEP = 2;

    public VerificationCodeEntryViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        (ByteString, string) mobileNumber,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IAuthRepository authRepository,
        IGlobalModalService globalModalService,
        AuthenticationFlowContext flowContext = AuthenticationFlowContext.REGISTRATION) : base(networkProvider,
        localizationService, globalModalService, connectivityService)
    {
        _mobileNumberIdentifier = mobileNumber.Item1;
        _mobileNumber = mobileNumber.Item2;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _authRepository = authRepository;
        _flowContext = flowContext;
        _localizationService = localizationService;
        _globalModalService = globalModalService;

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
            (code, time, isInOutage) =>
                !string.IsNullOrEmpty(code) &&
                code.Length == 6 &&
                code.All(char.IsDigit) &&
                time != AuthenticationConstants.EXPIRED_REMAINING_TIME &&
                !isInOutage
        );

        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canVerify);

        SendVerificationCodeCommand.ThrownExceptions
            .Subscribe(ex =>
            {
                if (!_isDisposed)
                {
                    PublishError(ex.Message);
                }
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
                if (!_isDisposed)
                {
                    PublishError(ex.Message);
                }

            })
            .DisposeWith(_disposables);

        LanguageChanged
            .StartWith(Unit.Default)
            .Subscribe(_ => UpdateDescriptionParts())
            .DisposeWith(_disposables);

        this.WhenActivated(disposables =>
        {
            OnViewLoaded().Subscribe().DisposeWith(disposables).DisposeWith(_disposables);

            this.WhenAnyValue(x => x.SecondsRemaining)
                .Select(FormatRemainingTime)
                .Subscribe(rt => RemainingTime = rt)
                .DisposeWith(disposables).DisposeWith(_disposables);

            this.WhenAnyValue(x => x.RemainingTime)
                .Select(time =>
                {
                    return $"Code expires in: {time}";
                })
                .ToPropertyEx(this, x => x.TimerHintText)
                .DisposeWith(disposables).DisposeWith(_disposables);
        });
    }

    [Reactive] public string DescriptionPreText { get; private set; } = string.Empty;
    [Reactive] public string DescriptionPostText { get; private set; } = string.Empty;

    public string FormattedMobileNumber => _mobileNumber;

    private void UpdateDescriptionParts()
    {
        string rawTemplate = _localizationService[LocalizationKeys.Authentication.SignUp.VerificationCodeEntry.DESCRIPTION];

        if (string.IsNullOrEmpty(rawTemplate))
        {
            DescriptionPreText = string.Empty;
            DescriptionPostText = string.Empty;
            return;
        }

        string[] parts = rawTemplate.Split(new[] { "{0}" }, StringSplitOptions.None);

        DescriptionPreText = parts.Length > 0 ? parts[0] : string.Empty;
        DescriptionPostText = parts.Length > 1 ? parts[1] : string.Empty;
    }

    public string StepBadgeText => _flowContext switch
    {
        AuthenticationFlowContext.REGISTRATION => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_STEPS),
        AuthenticationFlowContext.SECURE_KEY_RECOVERY => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_RECOVERY_STEPS),
        _ => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_STEPS)
    };

    [Reactive] public string CodeSentDescription { get; private set; } = string.Empty;

    [ObservableAsProperty] public string TimerHintText { get; } = string.Empty;

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
    private CountdownUpdateStatus CurrentStatus { get; set; } =
        CountdownUpdateStatus.Active;

    [Reactive] private bool IsMaxAttemptsReached { get; set; }

    [Reactive] private bool HasValidSession { get; set; }

    [ObservableAsProperty] public bool IsBusy { get; }

    [ObservableAsProperty] public bool IsResending { get; }

    private void PublishError(string message)
    {
        _executionErrorSubject.OnNext(message);
        ErrorMessage = message;
        HasError = !string.IsNullOrEmpty(message);
    }

    private long StartNewCountdownVersion()
    {
        Interlocked.Exchange(ref _alreadyVerifiedHandled, 0);
        Interlocked.Exchange(ref _autoRedirectVersion, 0);
        return Interlocked.Increment(ref _countdownVersion);
    }

    private void InvalidateCountdownCallbacks() => Interlocked.Increment(ref _countdownVersion);

    private bool IsCountdownVersionCurrent(long version) =>
        Volatile.Read(ref _countdownVersion) == version;

    private bool TryStartAutoRedirectOnce()
    {
        long currentVersion = Volatile.Read(ref _countdownVersion);
        return Interlocked.CompareExchange(ref _autoRedirectVersion, currentVersion, 0) == 0;
    }

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

        StartNewCountdownVersion();
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
            _cooldownTimer?.Dispose();
            _disposables.Dispose();
            _executionErrorSubject.Dispose();
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

            long countdownVersion = StartNewCountdownVersion();
            Task<Result<Ecliptix.Utilities.Unit, string>> initiateTask = CreateInitiateTask(countdownVersion);
            Result<Ecliptix.Utilities.Unit, string> result = await initiateTask;

            HandleInitiateResult(result);
        });
    }

    private Task<Result<Ecliptix.Utilities.Unit, string>> CreateInitiateTask(long countdownVersion)
    {
        CancellationToken cancellationToken = _cancellationTokenSource?.Token ?? CancellationToken.None;
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool> callback =
            CreateCountdownCallback(countdownVersion);

        return _flowContext == AuthenticationFlowContext.REGISTRATION
            ? _authRepository.InitiateRegistrationOtpAsync(_mobileNumberIdentifier,
                VerificationPurpose.Registration, callback, cancellationToken)
            : _authRepository.InitiateSecureKeyResetOtpAsync(_mobileNumberIdentifier, callback,
                cancellationToken);
    }

    private Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>
        CreateCountdownCallback(long countdownVersion)
    {
        return (seconds, identifier, status, message, messageKey, alreadyVerified) =>
            RxApp.MainThreadScheduler.Schedule(() =>
            {
                if (_isDisposed || !IsCountdownVersionCurrent(countdownVersion))
                {
                    return;
                }

                try
                {
                    HandleCountdownUpdate(seconds, identifier, status, message, messageKey, alreadyVerified);
                }
                catch (ObjectDisposedException)
                {

                }
            });
    }

    private void HandleInitiateResult(Result<Ecliptix.Utilities.Unit, string> result)
    {
        bool shouldSetError = result.IsErr
                              && !_isDisposed
                              && CurrentStatus != CountdownUpdateStatus
                                  .ServerUnavailable;

        if (shouldSetError)
        {
            PublishError(result.UnwrapErr());
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

        Task<Result<MembershipProto, string>> verifyTask = CreateVerifyTask(connectId, operationToken);
        Result<MembershipProto, string> result = await verifyTask;

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

    private void HandleNoValidSession() => PublishError(_localizationService[AuthenticationConstants.NO_VERIFICATION_SESSION_KEY]);

    private Task<Result<MembershipProto, string>> CreateVerifyTask(uint connectId, CancellationToken cancellationToken)
    {
        return _flowContext == AuthenticationFlowContext.REGISTRATION
            ? _authRepository.VerifyRegistrationOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                connectId,
                cancellationToken)
            : _authRepository.VerifySecureKeyResetOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                connectId,
                cancellationToken);
    }

    private async Task HandleSuccessfulVerification(MembershipProto membership, CancellationToken operationToken)
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

    private async Task StoreMembershipData(MembershipProto membership)
    {
        await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership.UniqueIdentifier);

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
            await _authRepository.CleanupRegistrationSessionAsync(VerificationSessionIdentifier!.Value)
                .WaitAsync(AuthenticationConstants.Timeouts.CleanupTimeout, cancellationToken);
        }
        else
        {
            await _authRepository
                .CleanupSecureKeyResetSessionAsync(VerificationSessionIdentifier!.Value)
                .WaitAsync(AuthenticationConstants.Timeouts.CleanupTimeout, cancellationToken);
        }
    }

    private void HandleVerificationError(string error) => PublishError(error);

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
        _initialTotalSeconds = null;
        ProgressValue = 1.0;
        long countdownVersion = StartNewCountdownVersion();

        CancellationTokenSource cancellationToken = CreateNewCancellationToken();

        Task.Run(async () =>
        {
            if (_isDisposed || cancellationToken.Token.IsCancellationRequested)
            {
                return;
            }

            cancellationToken.Token.ThrowIfCancellationRequested();

            Task<Result<Ecliptix.Utilities.Unit, string>> resendTask =
                CreateResendTask(cancellationToken.Token, countdownVersion);
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

    private Task<Result<Ecliptix.Utilities.Unit, string>> CreateResendTask(
        CancellationToken cancellationToken,
        long countdownVersion)
    {
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool> countdownCallback =
            CreateCountdownCallback(countdownVersion);

        return _flowContext == AuthenticationFlowContext.REGISTRATION
            ? _authRepository.ResendRegistrationOtpAsync(
                VerificationSessionIdentifier!.Value,
                _mobileNumberIdentifier,
                onCountdownUpdate: countdownCallback,
                cancellationToken: cancellationToken)
            : _authRepository.ResendSecureKeyResetOtpAsync(
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
                PublishError(error);
                StartAutoRedirectAsync(10, MembershipViewType.WELCOME_VIEW, error).ContinueWith(
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
                PublishError(error);
                SecondsRemaining = 0;
                CurrentStatus = CountdownUpdateStatus.Expired;
            }
        });
    }

    private void HandleNoActiveSession()
    {
        SecondsRemaining = 0;
        PublishError(_localizationService[AuthenticationConstants.NO_ACTIVE_VERIFICATION_SESSION_KEY]);
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

            PublishError(messageWithSeconds);
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
                    CurrentStatus = CountdownUpdateStatus.Expired;
                    _cooldownTimer?.Dispose();
                    _cooldownTimer = null;
                });
        }

        return 0;
    }

    private uint HandleExpiredStatus(string? message)
    {
        string errorMessage = !string.IsNullOrEmpty(message)
            ? message
            : _localizationService[AuthenticationConstants.VERIFICATION_SESSION_EXPIRED_KEY];

        PublishError(errorMessage);
        return 0;
    }

    private uint HandleMaxAttemptsStatus()
    {
        if (!TryStartAutoRedirectOnce())
        {
            return 0;
        }

        InvalidateCountdownCallbacks();
        CancelCurrentOperation();
        StopCooldownTimer();
        IsMaxAttemptsReached = true;
        HasValidSession = false;
        StartAutoRedirectAsync(10, MembershipViewType.WELCOME_VIEW).ContinueWith(
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
        if (!TryStartAutoRedirectOnce())
        {
            return 0;
        }

        InvalidateCountdownCallbacks();
        CancelCurrentOperation();
        StopCooldownTimer();
        HasValidSession = false;
        string message = _localizationService[AuthenticationConstants.SESSION_NOT_FOUND_KEY];
        StartAutoRedirectAsync(10, MembershipViewType.WELCOME_VIEW, message).ContinueWith(
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
        if (!TryStartAutoRedirectOnce())
        {
            return 0;
        }

        InvalidateCountdownCallbacks();
        CancelCurrentOperation();
        StopCooldownTimer();
        HasValidSession = false;
        string message = _localizationService[AuthenticationConstants.VERIFICATION_SESSION_EXPIRED_KEY];
        StartAutoRedirectAsync(10, MembershipViewType.WELCOME_VIEW, message).ContinueWith(
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
        HasError = true;
        HasValidSession = false;

        if (!string.IsNullOrWhiteSpace(error))
        {
            PublishError(error);
        }

        if (!TryStartAutoRedirectOnce())
        {
            return 0;
        }

        InvalidateCountdownCallbacks();
        CancelCurrentOperation();
        StopCooldownTimer();
        Task redirectTask = !string.IsNullOrEmpty(error)
            ? StartAutoRedirectAsync(10, MembershipViewType.WELCOME_VIEW, error)
            : StartAutoRedirectAsync(10, MembershipViewType.WELCOME_VIEW);

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

        return 0;
    }

    private uint HandleUnavailable(string? message)
    {
        HasError = true;
        HasValidSession = false;

        if (!TryStartAutoRedirectOnce())
        {
            return 0;
        }

        InvalidateCountdownCallbacks();
        CancelCurrentOperation();
        StopCooldownTimer();
        string errorMessage = !string.IsNullOrEmpty(message)
            ? message
            : _localizationService[LocalizationKeys.Common.SERVER_UNAVAILABLE];

        StartAutoRedirectAsync(10, MembershipViewType.WELCOME_VIEW, errorMessage).ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Log.Error(task.Exception, "[VERIFY-OTP] Unhandled exception in StartAutoRedirectAsync");
                }
            },
            TaskScheduler.Default);

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

        string title;
        string subtitle;

        if (IsMaxAttemptsReached)
        {
            title = _localizationService[LocalizationKeys.Verification.Redirect.Title.MAX_ATTEMPTS];
            subtitle = _localizationService[LocalizationKeys.Verification.Redirect.Subtitle.SECURITY_LIMIT];
        }
        else if (IsServerUnavailableError(message))
        {
            title = _localizationService[LocalizationKeys.Verification.Redirect.Title.SERVER_ERROR];
            subtitle = _localizationService[LocalizationKeys.Verification.Redirect.Subtitle.TRY_AGAIN];
        }
        else if (CurrentStatus == CountdownUpdateStatus.SessionExpired)
        {
            title = _localizationService[LocalizationKeys.Verification.Redirect.Title.SESSION_EXPIRED];
            subtitle = _localizationService[LocalizationKeys.Verification.Redirect.Subtitle.TIMEOUT];
        }
        else if (CurrentStatus == CountdownUpdateStatus.NotFound)
        {
            title = _localizationService[LocalizationKeys.Verification.Redirect.Title.SESSION_NOT_FOUND];
            subtitle = _localizationService[LocalizationKeys.Verification.Redirect.Subtitle.INVALID_STATE];
        }
        else
        {
            title = _localizationService[LocalizationKeys.Verification.Redirect.Title.GENERIC_ERROR];
            subtitle = _localizationService[LocalizationKeys.Verification.Redirect.Subtitle.RETURNING];
        }

        await StartAutoRedirectSequenceAsync(
            HostScreen,
            message,
            seconds,
            (hostViewModel) =>
            {
                CancelCurrentOperation();
                CleanupAndNavigate(hostViewModel, targetView);
            },
            title,
            subtitle
        );
    }

    private void StopCooldownTimer()
    {
        _cooldownTimer?.Dispose();
        _cooldownTimer = null;
    }

    private void CancelCurrentOperation()
    {
        CancellationTokenSource? cancellationTokenSource = Interlocked.Exchange(ref _cancellationTokenSource, null);
        cancellationTokenSource?.Cancel();
        cancellationTokenSource?.Dispose();
    }

    private void HandleCountdownUpdate(
        uint seconds,
        Guid identifier,
        CountdownUpdateStatus status,
        string? message,
        string? messageKey,
        bool alreadyVerified)
    {
        if (_isDisposed)
        {
            return;
        }

        if (!ValidateAndUpdateSessionIdentifier(identifier))
        {
            return;
        }

        if (alreadyVerified)
        {
            if (Interlocked.Exchange(ref _alreadyVerifiedHandled, 1) == 1)
            {
                return;
            }

            InvalidateCountdownCallbacks();
            ErrorMessage = string.Empty;
            HasError = false;
            HasValidSession = false;
            SecondsRemaining = 0;
            CurrentStatus = CountdownUpdateStatus.Expired;

            if (HostScreen is AuthenticationViewModel hostWindow)
            {
                NavigateToNextStep(hostWindow);
            }

            return;
        }

        string? normalizedMessageKey = NormalizeMessageKey(messageKey);
        string? resolvedMessage = ResolveCountdownMessage(message, normalizedMessageKey);
        bool isMaxAttemptsKey = normalizedMessageKey != null &&
                                IsMessageKey(normalizedMessageKey, VerificationMessageKeys.OTP_MAX_ATTEMPTS_REACHED);
        bool isRateLimitKey = IsRateLimitKey(normalizedMessageKey);

        if (_initialTotalSeconds == null && seconds > 0 && status == CountdownUpdateStatus.Active)
        {
            _initialTotalSeconds = seconds;
        }

        if (normalizedMessageKey != null)
        {
            if (IsMessageKey(normalizedMessageKey, VerificationMessageKeys.VERIFICATION_FLOW_EXPIRED))
            {
                status = CountdownUpdateStatus.SessionExpired;
            }
            else if (IsMessageKey(normalizedMessageKey, VerificationMessageKeys.OTP_EXPIRED))
            {
                status = CountdownUpdateStatus.Expired;
            }
            else if (IsMessageKey(normalizedMessageKey, VerificationMessageKeys.RESEND_COOLDOWN))
            {
                status = CountdownUpdateStatus.ResendCooldown;
            }
        }

        if (isMaxAttemptsKey || isRateLimitKey)
        {
            IsMaxAttemptsReached = true;
        }

        if (status == CountdownUpdateStatus.Failed &&
            TryExtractCooldownSeconds(resolvedMessage, out uint cooldownSeconds, out string? normalizedMessage))
        {
            status = CountdownUpdateStatus.ResendCooldown;
            seconds = cooldownSeconds;
            resolvedMessage = normalizedMessage;
        }

        if (status == CountdownUpdateStatus.Active && seconds == 0)
        {
            status = CountdownUpdateStatus.Expired;
        }

        if (status == CountdownUpdateStatus.Failed &&
            (isMaxAttemptsKey || isRateLimitKey || IsRateLimitMessage(normalizedMessageKey, resolvedMessage)))
        {
            IsMaxAttemptsReached = true;
        }
        else
        {
            IsMaxAttemptsReached = status == CountdownUpdateStatus.MaxAttemptsReached;
        }

        if (_initialTotalSeconds.HasValue && _initialTotalSeconds.Value > 0)
        {
            ProgressValue = (double)seconds / _initialTotalSeconds.Value;
        }
        else
        {
            ProgressValue = seconds > 0 ? 1.0 : 0.0;
        }

        CurrentStatus = status;
        SecondsRemaining = ProcessCountdownStatus(status, seconds, resolvedMessage);
    }

    private uint ProcessCountdownStatus(
        CountdownUpdateStatus status,
        uint seconds,
        string? message)
    {
        return status switch
        {
            CountdownUpdateStatus.Active => seconds,
            CountdownUpdateStatus.Expired => HandleExpiredStatus(message),
            CountdownUpdateStatus.ResendCooldown => HandleResendCooldown(message,
                seconds),
            CountdownUpdateStatus.Failed => HandleFailedStatus(message),
            CountdownUpdateStatus.NotFound => HandleNotFoundStatus(),
            CountdownUpdateStatus.MaxAttemptsReached => HandleMaxAttemptsStatus(),
            CountdownUpdateStatus.SessionExpired => HandleSessionExpiredStatus(),
            CountdownUpdateStatus.ServerUnavailable => HandleUnavailable(message),
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

    private static string FormatRemainingTime(uint seconds) => TimeSpan.FromSeconds(seconds).ToString(@"mm\:ss");

    private bool IsServerUnavailableError(string errorMessage)
    {
        string serverUnavailableText = _localizationService[LocalizationKeys.Common.SERVER_UNAVAILABLE];
        string serviceUnavailableText = _localizationService[ErrorI18NKeys.SERVICE_UNAVAILABLE];

        return errorMessage.Contains(serverUnavailableText, StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains(serviceUnavailableText, StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("unavailable", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("not responding", StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeMessageKey(string? messageKey) =>
        string.IsNullOrWhiteSpace(messageKey) ? null : messageKey.Trim();

    private static bool IsMessageKey(string messageKey, string expected) =>
        messageKey.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool IsRateLimitKey(string? messageKey)
    {
        if (messageKey is null)
        {
            return false;
        }

        return IsMessageKey(messageKey, VerificationMessageKeys.SECURITY_RATE_LIMIT_EXCEEDED)
               || IsMessageKey(messageKey, VerificationMessageKeys.DEVICE_RATE_LIMIT_EXCEEDED)
               || IsMessageKey(messageKey, VerificationMessageKeys.MOBILE_OTP_LIMIT_EXHAUSTED);
    }

    private static bool IsMissingLocalization(string value) =>
        value.Length > 1 && value.StartsWith('!') && value.EndsWith('!');

    private string? ResolveCountdownMessage(string? message, string? messageKey)
    {
        if (messageKey is null)
        {
            return message;
        }

        string? localized = null;

        if (IsMessageKey(messageKey, VerificationMessageKeys.OTP_MAX_ATTEMPTS_REACHED))
        {
            localized = _localizationService[LocalizationKeys.Verification.Error.MAX_ATTEMPTS_REACHED];
        }
        else if (IsRateLimitKey(messageKey))
        {
            localized = _localizationService[LocalizationKeys.Verification.Error.GLOBAL_RATE_LIMIT_EXCEEDED];
        }
        else if (IsMessageKey(messageKey, VerificationMessageKeys.OTP_EXPIRED) ||
                 IsMessageKey(messageKey, VerificationMessageKeys.VERIFICATION_FLOW_EXPIRED))
        {
            localized = _localizationService[LocalizationKeys.Verification.Error.SESSION_EXPIRED];
        }

        return string.IsNullOrWhiteSpace(localized) || IsMissingLocalization(localized) ? message : localized;
    }

    private bool IsRateLimitMessage(string? messageKey, string? message)
    {
        if (IsRateLimitKey(messageKey))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        string localizedRateLimit = _localizationService[LocalizationKeys.Verification.Error.GLOBAL_RATE_LIMIT_EXCEEDED];

        return (!string.IsNullOrEmpty(localizedRateLimit) &&
                message.Contains(localizedRateLimit, StringComparison.OrdinalIgnoreCase))
               || message.Contains("too many", StringComparison.OrdinalIgnoreCase)
               || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractCooldownSeconds(
        string? message,
        out uint seconds,
        out string? normalizedMessage)
    {
        seconds = 0;
        normalizedMessage = message;

        if (string.IsNullOrWhiteSpace(message))
        {
            return false;
        }

        int openIndex = message.LastIndexOf('(');
        int closeIndex = message.LastIndexOf(')');
        if (openIndex < 0 || closeIndex <= openIndex)
        {
            return false;
        }

        ReadOnlySpan<char> inner = message.AsSpan(openIndex + 1, closeIndex - openIndex - 1).Trim();
        if (inner.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            inner = inner[..^1].Trim();
        }

        if (!uint.TryParse(inner, out seconds))
        {
            return false;
        }

        normalizedMessage = message[..openIndex].TrimEnd();
        if (normalizedMessage.EndsWith(". ", StringComparison.Ordinal))
        {
            normalizedMessage = normalizedMessage[..^2].TrimEnd();
        }
        else if (normalizedMessage.EndsWith(".", StringComparison.Ordinal))
        {
            normalizedMessage = normalizedMessage[..^1].TrimEnd();
        }

        return true;
    }

    private async Task CleanupSessionAsync()
    {
        if (HasValidSession)
        {
            Guid sessionId = VerificationSessionIdentifier!.Value;

            if (_flowContext == AuthenticationFlowContext.REGISTRATION)
            {
                await _authRepository.CleanupRegistrationSessionAsync(sessionId);
            }
            else
            {
                await _authRepository.CleanupSecureKeyResetSessionAsync(sessionId);
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
            _cooldownTimer?.Dispose();
            _cooldownTimer = null;

            await _globalModalService.CloseAllAsync();

            _initialTotalSeconds = null;
            ProgressValue = 1.0;
            VerificationCode = string.Empty;
            ErrorMessage = string.Empty;
            HasError = false;
            SecondsRemaining = 0;
            RemainingTime = AuthenticationConstants.INITIAL_REMAINING_TIME;
            IsMaxAttemptsReached = false;
            CurrentStatus = CountdownUpdateStatus.Active;
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
        CountdownUpdateStatus status,
        uint cooldownBufferSeconds,
        bool isInNetworkOutage)
    {
        if (!hasValidSession || isInNetworkOutage)
        {
            return false;
        }

        if (status == CountdownUpdateStatus.ResendCooldown)
        {
            return cooldownBufferSeconds == 0;
        }

        if (secondsRemaining != 0)
        {
            return false;
        }

        return status == CountdownUpdateStatus.Expired;
    }
}
