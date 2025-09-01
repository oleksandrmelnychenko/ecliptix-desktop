using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class VerifyOtpViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly ByteString _phoneNumberIdentifier;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;

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
    [Reactive] public ulong SecondsRemaining { get; private set; }
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

        IObservable<bool> canResend = this.WhenAnyValue(x => x.SecondsRemaining, seconds => seconds == 0);
        ResendSendVerificationCodeCommand = ReactiveCommand.Create(ReSendVerificationCode, canResend);

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
            Result<Guid, string> result = await _registrationService.InitiateOtpVerificationAsync(
                _phoneNumberIdentifier,
                deviceIdentifier,
                onCountdownUpdate: seconds => RxApp.MainThreadScheduler.Schedule(() => SecondsRemaining = seconds)
            );

            if (result.IsErr)
            {
                ErrorMessage = result.UnwrapErr();
            }
            else
            {
                VerificationSessionIdentifier = result.Unwrap();
            }
        });
    }

    private async Task SendVerificationCode()
    {
        string systemDeviceIdentifier = SystemDeviceIdentifier();

        IsSent = true;
        ErrorMessage = string.Empty;

        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<Membership, string> result =
            await _registrationService.VerifyOtpAsync(
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

    private async void ReSendVerificationCode()
    {
        if (VerificationSessionIdentifier.HasValue)
        {
            ErrorMessage = string.Empty;
            HasError = false;
            
            string deviceIdentifier = SystemDeviceIdentifier();
            Result<Ecliptix.Utilities.Unit, string> result = await _registrationService.ResendOtpVerificationAsync(
                VerificationSessionIdentifier.Value,
                _phoneNumberIdentifier,
                deviceIdentifier);

            if (result.IsErr)
            {
                ErrorMessage = result.UnwrapErr();
                HasError = true;
            }
        }
        else
        {
            ErrorMessage = "No active verification session found";
            HasError = true;
        }
    }

    private static string FormatRemainingTime(ulong seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string formattedDataTimeString = time.ToString(@"mm\:ss");
        return formattedDataTimeString;
    }

    public void ResetState()
    {
        //VerificationCode = string.Empty; TODO make proper setting from outside for a segmented text box
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