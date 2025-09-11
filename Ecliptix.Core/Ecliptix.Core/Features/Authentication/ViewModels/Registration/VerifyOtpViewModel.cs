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
    private Guid? VerificationSessionIdentifier { get; set; } = null;

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

    [Reactive] public bool IsUiLocked { get; private set; } = false;
    [Reactive] public bool ShowDimmer { get; private set; } = false;
    [Reactive] public bool ShowSpinner { get; private set; } = false;

    public ReactiveCommand<Unit, Unit> TriggerAutoRedirectCommand { get; }
    
    private IDisposable? _autoRedirectTimer;
    private CancellationTokenSource? _streamCancellationSource;

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
            string deviceIdentifier = SystemDeviceIdentifier();
            Result<Ecliptix.Utilities.Unit, string> result = await _registrationService.InitiateOtpVerificationAsync(
                _phoneNumberIdentifier,
                deviceIdentifier,
                onCountdownUpdate: (seconds, identifier, status, message) =>
                    RxApp.MainThreadScheduler.Schedule(() =>
                    {
                        if (!VerificationSessionIdentifier.HasValue && identifier != Guid.Empty)
                        {
                            VerificationSessionIdentifier = identifier;
                        }

                        SecondsRemaining = status switch
                        {
                            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active => seconds,
                            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired => 0,
                            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed => HandleFailedStatus(
                                message),
                            VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound => HandleNotFoundStatus(),
                            VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached =>
                                HandleMaxAttemptsStatus(),
                            _ => Math.Min(seconds, SecondsRemaining)
                        };
                        CurrentStatus = status;
                    })
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

            _ = Task.Run(async () =>
            {
                Result<Ecliptix.Utilities.Unit, string> result = await _registrationService.ResendOtpVerificationAsync(
                    VerificationSessionIdentifier!.Value,
                    _phoneNumberIdentifier,
                    deviceIdentifier,
                    onCountdownUpdate: (seconds, identifier, status, message) =>
                        RxApp.MainThreadScheduler.Schedule(() =>
                        {
                            if (!VerificationSessionIdentifier.HasValue && identifier != Guid.Empty)
                            {
                                VerificationSessionIdentifier = identifier;
                            }

                            SecondsRemaining = status switch
                            {
                                VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active => seconds,
                                VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired => 0,
                                VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed => HandleFailedStatus(
                                    message),
                                VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound =>
                                    HandleNotFoundStatus(),
                                VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached =>
                                    HandleMaxAttemptsStatus(),
                                VerificationCountdownUpdate.Types.CountdownUpdateStatus.SessionExpired =>
                                    HandleNotFoundStatus(),
                                _ => Math.Min(seconds, SecondsRemaining)
                            };
                            CurrentStatus = status;
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
            });
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
        var redirectViewModel = new RedirectNotificationViewModel(message, seconds, onComplete, _localizationService);
        var redirectView = new RedirectNotificationView { DataContext = redirectViewModel };
        
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
    private void CleanupAndNavigate(MembershipViewType targetView)
    {
        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            _ = Task.Run(async () =>
            {
                await hostWindow.HideBottomSheetAsync();
            });
        }
        FireAndForgetCleanup();
        ((MembershipHostWindowModel)HostScreen).Navigate.Execute(targetView).Subscribe();
    }

    private void FireAndForgetCleanup()
    {
        _ = Task.Run(async () =>
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(5));
            await CleanupVerificationSession().WaitAsync(cts.Token);
        });
    }

    private async Task CleanupVerificationSession()
    {
        if (HasValidSession)
        {
            await _streamCancellationSource?.CancelAsync()!;

            try
            {
                await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier!.Value);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to cleanup verification session: {Error}", ex.Message);
            }

            try
            {
                await NetworkProvider.RemoveProtocolForTypeAsync(PubKeyExchangeType.ServerStreaming);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to remove stream protocol: {Error}", ex.Message);
            }

            VerificationSessionIdentifier = null;
        }

        _streamCancellationSource?.Dispose();
        _streamCancellationSource = null;
    }

    private static string FormatRemainingTime(uint seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string formattedDataTimeString = time.ToString(@"mm\:ss");
        return formattedDataTimeString;
    }

    public async void HandleEnterKeyPress()
    {
        if (SendVerificationCodeCommand != null && await SendVerificationCodeCommand.CanExecute.FirstOrDefaultAsync())
        {
            SendVerificationCodeCommand.Execute().Subscribe();
        }
    }

    public void ResetState()
    {
        _autoRedirectTimer?.Dispose();
        _autoRedirectTimer = null;
        
        IsUiLocked = false;
        ShowDimmer = false;
        ShowSpinner = false;
        
        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await hostWindow.HideBottomSheetAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to hide bottom sheet during reset");
                }
            });
        }
        
        if (HasValidSession)
        {
            _ = Task.Run(async () => { await CleanupVerificationSession(); });
        }
        else
        {
            _streamCancellationSource?.Cancel();
            _streamCancellationSource?.Dispose();
            _streamCancellationSource = null;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await NetworkProvider.RemoveProtocolForTypeAsync(PubKeyExchangeType.ServerStreaming);
            }
            catch (Exception ex)
            {
                Log.Warning("Failed to remove stream protocol in ResetState: {Error}", ex.Message);
            }
        });

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
        VerificationSessionIdentifier = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _autoRedirectTimer?.Dispose();
            if (HasValidSession)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await CleanupVerificationSession();
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to cleanup verification session in Dispose: {Error}", ex.Message);
                    }

                    try
                    {
                        await NetworkProvider.RemoveProtocolForTypeAsync(PubKeyExchangeType.ServerStreaming);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Failed to remove stream protocol in Dispose: {Error}", ex.Message);
                    }
                });
            }
            else
            {
                _streamCancellationSource?.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}