using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.ViewModels.Memberships.SignUp;

public class VerifyOtpViewModel : ViewModelBase, IRoutableViewModel
{
    private readonly ByteString _phoneNumberIdentifier;
    private string _errorMessage = string.Empty;
    private bool _isSent;
    private string _remainingTime = "01:00";
    private string _verificationCode;
    private IApplicationSecureStorageProvider _applicationSecureStorageProvider;

    public string? UrlPathSegment { get; } = "/verification-code-entry";
    public IScreen HostScreen { get; }
    private Guid? VerificationSessionIdentifier { get; set; } = null;

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }

    public ReactiveCommand<Unit, Unit> NavToPasswordConfirmation { get; }

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
        private set
        {
            Console.WriteLine($"Setting RemainingTime to: '{value}'");
            this.RaiseAndSetIfChanged(ref _remainingTime, value);
        }
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
        ISystemEvents systemEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        ByteString phoneNumberIdentifier,
        IApplicationSecureStorageProvider applicationSecureStorageProvider) : base(systemEvents, networkProvider,
        localizationService)
    {
        _phoneNumberIdentifier = phoneNumberIdentifier;
        _verificationCode = string.Empty;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;

        HostScreen = hostScreen;

        NavToPasswordConfirmation = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.ConfirmSecureKey);
        });

        IObservable<bool> canVerify = this.WhenAnyValue(
            x => x.VerificationCode,
            x => x.RemainingTime,
            (code, time) => code?.Length == 6 && code.All(char.IsDigit) && time != "00:00"
        );
        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canVerify);

        // "RESEND" button enabled only when timer is zero
        IObservable<bool> canResend = this.WhenAnyValue(x => x.SecondsRemaining, seconds => seconds == 0);
        canResend.Subscribe(value => Console.WriteLine($"canResend: {value}")); // For debugging
        ResendSendVerificationCodeCommand = ReactiveCommand.Create(ReSendVerificationCode, canResend);

        this.WhenActivated(disposables => { OnViewLoaded().Subscribe().DisposeWith(disposables); });
    }

    private IObservable<Unit> OnViewLoaded()
    {
        return Observable.FromAsync(async () =>
        {
            await InitiateVerification(
                _phoneNumberIdentifier,
                InitiateVerificationRequest.Types.Type.SendOtp
            );
        });
    }

    private async Task InitiateVerification(ByteString phoneNumberIdentifier,
        InitiateVerificationRequest.Types.Type type)
    {
        using CancellationTokenSource cancellationTokenSource = new();

        string? systemDeviceIdentifier = SystemDeviceIdentifier();

        InitiateVerificationRequest membershipVerificationRequest = new()
        {
            PhoneNumberIdentifier = phoneNumberIdentifier,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(systemDeviceIdentifier)),
            Purpose = VerificationPurpose.Registration,
            Type = type
        };

        uint connectId = ComputeConnectId();
        _ = await NetworkProvider.ExecuteReceiveStreamRequestAsync(
            connectId,
            RpcServiceType.InitiateVerification,
            SecureByteStringInterop.WithByteStringAsSpan(membershipVerificationRequest.ToByteString(),
                span => span.ToArray()),
            payload =>
            {
                VerificationCountdownUpdate timerTick =
                    Helpers.ParseFromBytes<VerificationCountdownUpdate>(payload);
                if (timerTick.AlreadyVerified)
                {
                }

                if (timerTick.Status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed)
                {
                }

                if (timerTick.Status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired)
                {
                    // Notify user and redirect to the Phone verification view
                }

                if (timerTick.Status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached)
                {
                    // Notify user and redirect to the Phone verification view
                }

                if (timerTick.Status == VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound)
                {
                    // Notify user and redirect to the Phone verification view
                }

                VerificationSessionIdentifier ??= Helpers.FromByteStringToGuid(timerTick.SessionIdentifier);
                RxApp.MainThreadScheduler.Schedule(() => SecondsRemaining = timerTick.SecondsRemaining);
                RxApp.MainThreadScheduler.Schedule(() =>
                    RemainingTime = FormatRemainingTime(timerTick.SecondsRemaining));

                return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value));
            }, true,
            cancellationTokenSource.Token
        );
    }

    private async Task SendVerificationCode()
    {
        string? systemDeviceIdentifier = SystemDeviceIdentifier();

        IsSent = true;
        ErrorMessage = string.Empty;

        VerifyCodeRequest verifyCodeRequest = new()
        {
            Code = VerificationCode,
            Purpose = VerificationPurpose.Registration,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(systemDeviceIdentifier))
        };

        _ = await NetworkProvider.ExecuteUnaryRequestAsync(
            ComputeConnectId(),
            RpcServiceType.VerifyOtp,
            SecureByteStringInterop.WithByteStringAsSpan(verifyCodeRequest.ToByteString(),
                span => span.ToArray()),
            async payload =>
            {
                VerifyCodeResponse verifyCodeReply = Helpers.ParseFromBytes<VerifyCodeResponse>(payload);
                if (verifyCodeReply.Result == VerificationResult.Succeeded)
                {
                    Membership membership = verifyCodeReply.Membership;


                    await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);


                    NavToPasswordConfirmation.Execute().Subscribe();
                }
                else if (verifyCodeReply.Result == VerificationResult.InvalidOtp)
                {
                }

                return Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value);
            }, true,
            CancellationToken.None
        );
    }

    private void ReSendVerificationCode()
    {
        Task.Run(async () => await InitiateVerification(
            _phoneNumberIdentifier,
            InitiateVerificationRequest.Types.Type.ResendOtp));
    }

    private string FormatRemainingTime(ulong seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string t = time.ToString(@"mm\:ss");
        Console.WriteLine(t);
        return t;
    }
}