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
    private CancellationTokenSource? _streamCancellationSource;
    private Task? _resendTask;
    private readonly SemaphoreSlim _cleanupSemaphore = new(1, 1);
    private CancellationTokenSource? _cleanupCts;
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

            await SafeDisposeStreamCancellationSource();
            _streamCancellationSource = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            _disposables.Add(_streamCancellationSource);

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
                cancellationToken: _streamCancellationSource.Token
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

        try
        {
            using CancellationTokenSource timeoutCts = new(AuthenticationConstants.Timeouts.OtpVerificationTimeout);
            using CancellationTokenSource combinedCts = CancellationTokenSource.CreateLinkedTokenSource(
                timeoutCts.Token,
                _streamCancellationSource?.Token ?? CancellationToken.None);

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

                await SafeDisposeStreamCancellationSource();

                if (HasValidSession)
                {
                    try
                    {
                        await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier!.Value)
                            .WaitAsync(AuthenticationConstants.Timeouts.VerificationCleanupTimeout, combinedCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Log.Warning("Verification session cleanup was cancelled");
                    }
                    catch (TimeoutException)
                    {
                        Log.Warning("Verification session cleanup timed out");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to cleanup verification session after success: {Error}", ex.Message);
                    }
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
        catch (OperationCanceledException) when (_isDisposed)
        {
        }
        catch (TimeoutException)
        {
            if (!_isDisposed)
            {
                Log.Warning("OTP verification timed out after {Timeout}ms",
                    AuthenticationConstants.Timeouts.OtpVerificationTimeout.TotalMilliseconds);
                ErrorMessage = _localizationService["Errors.VerificationTimeout"];
                IsSent = false;
            }
        }
        catch (Exception ex)
        {
            if (!_isDisposed)
            {
                Log.Warning("OTP verification failed: {Error}", ex.Message);
                ErrorMessage = _localizationService["Errors.VerificationFailed"];
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

            CancellationTokenSource? linkedCts;
            try
            {
                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_streamCancellationSource?.Token ??
                                                                            CancellationToken.None);
                _disposables.Add(linkedCts);
            }
            catch (ObjectDisposedException)
            {
                return Task.CompletedTask;
            }

            _resendTask = Task.Run(async () =>
            {
                try
                {
                    if (_isDisposed || linkedCts.Token.IsCancellationRequested)
                        return;

                    linkedCts.Token.ThrowIfCancellationRequested();

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
                                }));

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
            }, linkedCts.Token);
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
            ErrorMessage = _localizationService[error];
        }

        HasValidSession = false;
        return 0;
    }

    private void StartAutoRedirect(int seconds, MembershipViewType targetView)
    {
        if (_isDisposed) return;

        // Cancel any existing redirect first
        _autoRedirectTimer?.Dispose();
        _autoRedirectTimer = null;

        // Reset UI state before starting new redirect
        IsUiLocked = false;
        ShowDimmer = false;
        ShowSpinner = false;

        // Now start the new redirect
        AutoRedirectCountdown = seconds;
        IsUiLocked = true;
        ShowDimmer = true;
        ShowSpinner = true;

        string key = IsMaxAttemptsReached
            ? AuthenticationConstants.MaxAttemptsReachedKey
            : AuthenticationConstants.SessionNotFoundKey;

        string message = _localizationService.GetString(key);

        ShowRedirectNotification(message, seconds, () =>
        {
            if (!_isDisposed)
                CleanupAndNavigate(targetView);
        });
    }

    private void ShowRedirectNotification(string message, int seconds, Action onComplete)
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
                        await hostWindow.ShowRedirectNotificationAsync(redirectView, isDismissable: false);
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

    private void CleanupAndNavigate(MembershipViewType targetView)
    {
        if (_isDisposed) return;

        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    if (!_isDisposed)
                        await hostWindow.HideBottomSheetAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning("Failed to hide bottom sheet during navigation: {Error}", ex.Message);
                }
            });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_isDisposed)
                    await PerformCleanupAsync();
            }
            catch (Exception ex)
            {
                Log.Warning("Cleanup during navigation failed: {Error}", ex.Message);
            }
        });

        if (!_isDisposed && HostScreen is MembershipHostWindowModel membershipHostWindow)
        {
            membershipHostWindow.Navigate.Execute(targetView).Subscribe();
            membershipHostWindow.ClearNavigationStack();
        }
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

        try
        {
            if (await SendVerificationCodeCommand.CanExecute.FirstOrDefaultAsync())
            {
                SendVerificationCodeCommand.Execute().Subscribe().DisposeWith(_disposables);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("HandleEnterKeyPress failed: {Error}", ex.Message);
        }
    }

    private async Task SafeDisposeStreamCancellationSource()
    {
        if (_streamCancellationSource != null)
        {
            try
            {
                if (!_streamCancellationSource.IsCancellationRequested)
                    await _streamCancellationSource.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
            }
            finally
            {
                _streamCancellationSource?.Dispose();
                _streamCancellationSource = null;
            }
        }
    }

    private async Task PerformCleanupAsync(bool isDisposing = false)
    {
        TimeSpan waitTimeout = isDisposing
            ? AuthenticationConstants.Timeouts.DefaultCleanupTimeout
            : TimeSpan.Zero;

        if (!await _cleanupSemaphore.WaitAsync(waitTimeout))
        {
            return;
        }

        try
        {
            if (_cleanupCts is not null)
            {
                try
                {
                    await _cleanupCts.CancelAsync();
                }
                catch (ObjectDisposedException)
                {
                }
                finally
                {
                    _cleanupCts.Dispose();
                }
            }

            _cleanupCts = new CancellationTokenSource();
            CancellationToken cleanupToken = _cleanupCts.Token;

            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            await SafeDisposeStreamCancellationSource();

            if (_resendTask is { IsCompleted: false })
            {
                try
                {
                    await _resendTask.WaitAsync(AuthenticationConstants.Timeouts.TaskWaitTimeout, cleanupToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (TimeoutException)
                {
                    Log.Warning("Resend task cleanup timed out after {Timeout}ms",
                        AuthenticationConstants.Timeouts.TaskWaitTimeout.TotalMilliseconds);
                }
                catch (Exception ex)
                {
                    Log.Warning("Resend task cancellation failed: {Error}", ex.Message);
                }
            }

            if (HasValidSession && !cleanupToken.IsCancellationRequested)
            {
                try
                {
                    Guid sessionId = VerificationSessionIdentifier!.Value;
                    await _registrationService.CleanupVerificationSessionAsync(sessionId)
                        .WaitAsync(AuthenticationConstants.Timeouts.VerificationCleanupTimeout, cleanupToken);
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Session cleanup cancelled");
                }
                catch (TimeoutException)
                {
                    Log.Warning("Session cleanup timed out");
                }
                catch (Exception ex)
                {
                    Log.Warning("Session cleanup failed (non-critical): {Error}", ex.Message);
                }
            }

            _resendTask = null;

            if (!isDisposing && !cleanupToken.IsCancellationRequested)
            {
                if (!_isDisposed)
                {
                    await ResetUiState();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Critical error during cleanup operation");
        }
        finally
        {
            _cleanupSemaphore.Release();
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

        _ = Task.Run(async () =>
        {
            try
            {
                if (!_isDisposed)
                    await PerformCleanupAsync(true);
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
                Task.Run(async () =>
                {
                    try
                    {
                        await PerformCleanupAsync(true);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Dispose cleanup failed: {Error}", ex.Message);
                    }
                    finally
                    {
                        try
                        {
                            _disposables?.Dispose();
                            _cleanupSemaphore?.Dispose();
                            if (_cleanupCts != null)
                            {
                                await _cleanupCts.CancelAsync();
                                _cleanupCts.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning("Final cleanup failed: {Error}", ex.Message);
                        }
                    }
                }).Wait(AuthenticationConstants.Timeouts.DisposeTimeout);
            }
            catch (Exception ex)
            {
                Log.Warning("Dispose operation failed: {Error}", ex.Message);
            }
        }

        base.Dispose(disposing);
    }
}