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
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class VerifyOtpViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private readonly ByteString _phoneNumberIdentifier;
    private string _errorMessage = string.Empty;
    private bool _isSent;
    private string _remainingTime = AuthenticationConstants.InitialRemainingTime;
    private string _verificationCode;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;



    public string? UrlPathSegment { get; } = "/verification-code-entry";
    public IScreen HostScreen { get; }
    private Guid? VerificationSessionIdentifier { get; set; } = null;

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }

    public ReactiveCommand<Unit, IRoutableViewModel> NavToPasswordConfirmation { get; }

    public new ViewModelActivator Activator { get; } = new();

    private ulong _secondsRemaining;

    public string VerificationCode
    {
        get => _verificationCode;
        set => this.RaiseAndSetIfChanged(ref _verificationCode, value);
    }

    public bool IsSent
    {
        get => _isSent;
        private set => this.RaiseAndSetIfChanged(ref _isSent, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public string RemainingTime
    {
        get => _remainingTime;
        private set => this.RaiseAndSetIfChanged(ref _remainingTime, value);
    }

    public ulong SecondsRemaining
    {
        get => _secondsRemaining;
        private set
        {
            this.RaiseAndSetIfChanged(ref _secondsRemaining, value);
            RemainingTime = FormatRemainingTime(value);
        }
    }

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
        _verificationCode = string.Empty;
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
            (code, time) => code?.Length == 6 && code.All(char.IsDigit) &&
                            time != AuthenticationConstants.ExpiredRemainingTime
        );
        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canVerify);

        IObservable<bool> canResend = this.WhenAnyValue(x => x.SecondsRemaining, seconds => seconds == 0);
        ResendSendVerificationCodeCommand = ReactiveCommand.Create(ReSendVerificationCode, canResend);

        this.WhenActivated(disposables => { OnViewLoaded().Subscribe().DisposeWith(disposables); });
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
        string? systemDeviceIdentifier = SystemDeviceIdentifier();

        IsSent = true;
        ErrorMessage = string.Empty;

        uint connectId = ComputeConnectId();

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

    private void ReSendVerificationCode()
    {
        SecondsRemaining = 60;
        OnViewLoaded().Subscribe();
    }

    private static string FormatRemainingTime(ulong seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string formattedDataTimeString = time.ToString(@"mm\:ss");
        return formattedDataTimeString;
    }

    public void ResetState()
    {
        _errorMessage = string.Empty;
        _isSent = false;
        this.RaisePropertyChanged(nameof(ErrorMessage));
        this.RaisePropertyChanged(nameof(IsSent));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && VerificationSessionIdentifier.HasValue)
        {
            _ = Task.Run(async () =>
                await _registrationService.CleanupVerificationSessionAsync(VerificationSessionIdentifier.Value));
        }

        base.Dispose(disposing);
    }
}