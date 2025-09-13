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

    public ReactiveCommand<Unit, Unit> TriggerAutoRedirectCommand { get; }

    private IDisposable? _autoRedirectTimer;
    private CancellationTokenSource? _streamCancellationSource;
    private Task? _resendTask;
    private readonly SemaphoreSlim _cleanupSemaphore = new(1, 1);
    private CancellationTokenSource? _cleanupCts;

    private bool HasValidSession =>
        VerificationSessionIdentifier.HasValue &&
        VerificationSessionIdentifier.Value != Guid.Empty;

    public VerifyOtpViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        ByteString phoneNumberIdentifier,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService) : base(systemEventService, networkProvider,
        localizationService)
    {
        _phoneNumberIdentifier = phoneNumberIdentifier;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _registrationService = registrationService;
        _localizationService = localizationService;

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

        IObservable<bool> canResend = this.WhenAnyValue(x => x.SecondsRemaining)
            .Select(seconds => seconds == 0)
            .Catch<bool, Exception>(ex => Observable.Return(true));
        ResendSendVerificationCodeCommand = ReactiveCommand.CreateFromTask(ReSendVerificationCode, canResend);

        this.WhenActivated(disposables =>
        {
            OnViewLoaded().Subscribe().DisposeWith(disposables);

            this.WhenAnyValue(x => x.ErrorMessage)
                .Select(e => !string.IsNullOrEmpty(e))
                .DistinctUntilChanged()
                .Subscribe(flag => HasError = flag)
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.SecondsRemaining)
                .Select(FormatRemainingTime)
                .Subscribe(rt => RemainingTime = rt)
                .DisposeWith(disposables);
        });

        TriggerAutoRedirectCommand = ReactiveCommand.Create(() =>
        {
            StartAutoRedirect(5, MembershipViewType.Welcome);
        });
    }

    private IObservable<Unit> OnViewLoaded()
    {
        return Observable.FromAsync(async () =>
        {
            _streamCancellationSource?.Dispose();
            _streamCancellationSource = new CancellationTokenSource();

            string deviceIdentifier = SystemDeviceIdentifier();
            Result<Ecliptix.Utilities.Unit, string> result = await _registrationService.InitiateOtpVerificationAsync(
                _phoneNumberIdentifier,
                deviceIdentifier,
                onCountdownUpdate: (seconds, identifier, status, message) =>
                    RxApp.MainThreadScheduler.Schedule(() =>
                    {
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

            if (result.IsErr)
            {
                ErrorMessage = result.UnwrapErr();
            }
        });
    }

    private async Task SendVerificationCode()
    {
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

        Result<Membership, string> result =
            await _registrationService.VerifyOtpAsync(
                VerificationSessionIdentifier!.Value,
                VerificationCode,
                systemDeviceIdentifier,
                connectId);

        if (result.IsOk)
        {
            Membership membership = result.Unwrap();
            await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);

            if (HasValidSession)
            {
                await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier!.Value);
            }

            NavToPasswordConfirmation.Execute().Subscribe();
        }
        else
        {
            ErrorMessage = result.UnwrapErr();
            IsSent = false;
        }
    }

    private Task ReSendVerificationCode()
    {
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
            }
            catch (ObjectDisposedException)
            {
                return Task.CompletedTask;
            }

            _resendTask = Task.Run(async () =>
            {
                try
                {
                    linkedCts.Token.ThrowIfCancellationRequested();

                    Result<Ecliptix.Utilities.Unit, string> result =
                        await _registrationService.ResendOtpVerificationAsync(
                            VerificationSessionIdentifier!.Value,
                            _phoneNumberIdentifier,
                            deviceIdentifier,
                            onCountdownUpdate: (seconds, identifier, status, message) =>
                                RxApp.MainThreadScheduler.Schedule(() =>
                                {
                                    HandleCountdownUpdate(seconds, identifier, status, message);
                                }));

                    if (result.IsErr)
                    {
                        RxApp.MainThreadScheduler.Schedule(() =>
                        {
                            ErrorMessage = result.UnwrapErr();
                            HasError = true;
                            SecondsRemaining = 0;
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.Warning("Resend verification failed: {Error}", ex.Message);
                }
                finally
                {
                    linkedCts?.Dispose();
                }
            }, linkedCts.Token);
        }
        else
        {
            SecondsRemaining = 0;
            ErrorMessage = _localizationService[AuthenticationConstants.NoActiveVerificationSessionKey];
            HasError = true;
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

        return 0;
    }

    private void StartAutoRedirect(int seconds, MembershipViewType targetView)
    {
        _autoRedirectTimer?.Dispose();
        AutoRedirectCountdown = seconds;

        IsUiLocked = true;
        ShowDimmer = true;
        ShowSpinner = true;

        string key = IsMaxAttemptsReached
            ? AuthenticationConstants.MaxAttemptsReachedKey
            : AuthenticationConstants.SessionNotFoundKey;

        string message = _localizationService.GetString(key)
                         ?? _localizationService[key]
                         ?? (IsMaxAttemptsReached ? "Maximum attempts reached" : "Session expired");

        ShowRedirectNotification(message, seconds, () => CleanupAndNavigate(targetView));
    }

    private void ShowRedirectNotification(string message, int seconds, Action onComplete)
    {
        RedirectNotificationViewModel redirectViewModel = new(message, seconds, onComplete, _localizationService);
        RedirectNotificationView redirectView = new() { DataContext = redirectViewModel };

        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await hostWindow.ShowRedirectNotificationAsync(redirectView, isDismissable: false);
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
        if (identifier != Guid.Empty)
        {
            lock (_sessionLock)
            {
                if (_verificationSessionIdentifier == Guid.Empty)
                {
                    _verificationSessionIdentifier = identifier;
                }
                else if (_verificationSessionIdentifier != identifier)
                {
                    Log.Warning("Attempted to overwrite session identifier {Previous} with {New}",
                        _verificationSessionIdentifier, identifier);
                }
            }
        }

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
        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            _ = Task.Run(async () => { await hostWindow.HideBottomSheetAsync(); });
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await PerformCleanupAsync();
            }
            catch (Exception ex)
            {
                Log.Warning("Cleanup during navigation failed: {Error}", ex.Message);
            }
        });
        ((MembershipHostWindowModel)HostScreen).Navigate.Execute(targetView).Subscribe();
        ((MembershipHostWindowModel)HostScreen).ClearNavigationStack();
    }

    private static string FormatRemainingTime(uint seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string formattedDataTimeString = time.ToString(@"mm\:ss");
        return formattedDataTimeString;
    }

    public async void HandleEnterKeyPress()
    {
        if (await SendVerificationCodeCommand.CanExecute.FirstOrDefaultAsync())
        {
            SendVerificationCodeCommand.Execute().Subscribe();
        }
    }

    private async Task PerformCleanupAsync(bool isDisposing = false)
    {
        if (!await _cleanupSemaphore.WaitAsync(0))
            return;

        try
        {
            if (_cleanupCts is not null)
            {
                await _cleanupCts.CancelAsync();
                _cleanupCts.Dispose();
            }

            _cleanupCts = new CancellationTokenSource();
            CancellationToken cleanupToken = _cleanupCts.Token;

            _autoRedirectTimer?.Dispose();
            _autoRedirectTimer = null;

            await _streamCancellationSource?.CancelAsync()!;

            if (_resendTask is { IsCompleted: false })
            {
                try
                {
                    await _resendTask.WaitAsync(TimeSpan.FromSeconds(1), cleanupToken);
                }
                catch (OperationCanceledException)
                {
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
                        .WaitAsync(TimeSpan.FromSeconds(2), cleanupToken);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.Warning("Failed to cleanup verification session: {Error}", ex.Message);
                }
            }

            _streamCancellationSource?.Dispose();
            _streamCancellationSource = null;
            _resendTask = null;

            if (!isDisposing && !cleanupToken.IsCancellationRequested)
            {
                ResetUiState();
            }
        }
        finally
        {
            _cleanupSemaphore.Release();
        }
    }

    private void ResetUiState()
    {
        _autoRedirectTimer?.Dispose();
        _autoRedirectTimer = null;

        IsUiLocked = false;
        ShowDimmer = false;
        ShowSpinner = false;

        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            _ = Task.Run(async () => { await hostWindow.HideBottomSheetAsync(); });
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
        }
    }

    public void ResetState()
    {
        _ = Task.Run(async () => { await PerformCleanupAsync(); });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _ = Task.Run(async () =>
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
                    _cleanupSemaphore?.Dispose();
                    await _cleanupCts?.CancelAsync()!;
                    _cleanupCts?.Dispose();
                }
            });
        }

        base.Dispose(disposing);
    }
}