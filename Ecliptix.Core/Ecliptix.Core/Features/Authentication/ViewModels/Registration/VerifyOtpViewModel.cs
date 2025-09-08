using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
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
            .Catch<bool, Exception>(ex =>
            {
                Log.Error(ex, "Error in canResend observable");
                return Observable.Return(true);
            });
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
    }

    private IObservable<Unit> OnViewLoaded()
    {
        return Observable.FromAsync(async () =>
        {
            string deviceIdentifier = SystemDeviceIdentifier();
            Result<Ecliptix.Utilities.Unit, string> result = await _registrationService.InitiateOtpVerificationAsync(
                _phoneNumberIdentifier,
                deviceIdentifier,
                onCountdownUpdate: (seconds, identifier, status) =>
                    RxApp.MainThreadScheduler.Schedule(() =>
                    {
                        VerificationSessionIdentifier ??= identifier;

                        SecondsRemaining = status switch
                        {
                            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active => seconds,
                            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired
                                or VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed
                                or VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound
                                or VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached => 0,
                            _ => Math.Min(seconds, SecondsRemaining)
                        };
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

        if (!VerificationSessionIdentifier.HasValue)
        {
            IsSent = false;
            HasError = true;
            ErrorMessage = _localizationService[AuthenticationConstants.NoVerificationSessionKey];
            Log.Warning("[VERIFY-OTP] Attempted to send verification code with no active session");
            return;
        }
        
        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<Membership, string> result =
            await _registrationService.VerifyOtpAsync(
                VerificationSessionIdentifier.Value,
                VerificationCode,
                systemDeviceIdentifier,
                connectId);

        if (result.IsOk)
        {
            Membership membership = result.Unwrap();
            await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);

            if (VerificationSessionIdentifier.HasValue)
            {
                await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier.Value);
            }

            NavToPasswordConfirmation.Execute().Subscribe();
        }
        else
        {
            ErrorMessage = result.UnwrapErr();
            IsSent = false;
        }
    }

    private async Task ReSendVerificationCode()
    {
        if (VerificationSessionIdentifier.HasValue)
        {
            ErrorMessage = string.Empty;
            HasError = false;

            string deviceIdentifier = SystemDeviceIdentifier();
            
            try
            {
                Log.Information("[VERIFY-OTP] Starting resend OTP verification");
                
                using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(70));
                
                try
                {
                    Result<Ecliptix.Utilities.Unit, string> result = await _registrationService.ResendOtpVerificationAsync(
                        VerificationSessionIdentifier.Value,
                        _phoneNumberIdentifier,
                        deviceIdentifier,
                        onCountdownUpdate: (seconds, identifier, status) =>
                            RxApp.MainThreadScheduler.Schedule(() =>
                            {
                                VerificationSessionIdentifier ??= identifier;
                                
                                Log.Information("[VERIFY-OTP] Countdown update: {Seconds}s, Status: {Status}", seconds, status);

                                SecondsRemaining = status switch
                                {
                                    VerificationCountdownUpdate.Types.CountdownUpdateStatus.Active => seconds,
                                    VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired
                                        or VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed
                                        or VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound
                                        or VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached => 0,
                                    _ => Math.Min(seconds, SecondsRemaining)
                                };
                            })).WaitAsync(timeoutCts.Token);

                    Log.Information("[VERIFY-OTP] Resend OTP completed normally");
                    
                    if (result.IsErr)
                    {
                        ErrorMessage = result.UnwrapErr();
                        HasError = true;
                    }
                }
                catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested)
                {
                    Log.Information("[VERIFY-OTP] Resend OTP timed out after 70 seconds - this is expected");
                }
                
                Log.Information("[VERIFY-OTP] Ensuring SecondsRemaining = 0 and button re-enabled");
                SecondsRemaining = 0;
            }
            catch (Exception ex)
            {
                SecondsRemaining = 0;
                ErrorMessage = ex.Message;
                HasError = true;
                Log.Error(ex, "Error during OTP resend");
            }
        }
        else
        {
            SecondsRemaining = 0;
            ErrorMessage = "No active verification session found";
            HasError = true;
        }
    }


    private static string FormatRemainingTime(uint seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string formattedDataTimeString = time.ToString(@"mm\:ss");
        return formattedDataTimeString;
    }

    public void ResetState()
    {
        VerificationCode = "";
        IsSent = false;
        ErrorMessage = string.Empty;
        HasError = false;
        SecondsRemaining = 0;
        RemainingTime = AuthenticationConstants.InitialRemainingTime;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && VerificationSessionIdentifier.HasValue)
        {
            _ = Task.Run(async () =>
            {
                await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier.Value);
                await NetworkProvider.RemoveProtocolForTypeAsync(PubKeyExchangeType.ServerStreaming);
            });
        }

        base.Dispose(disposing);
    }
}