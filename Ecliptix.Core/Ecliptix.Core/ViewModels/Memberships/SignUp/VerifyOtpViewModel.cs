using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.ViewModels.Memberships.SignUp;

public class VerifyOtpViewModel : ViewModelBase, IRoutableViewModel
{
    private readonly string _mobileNumber;
    private readonly IDisposable _mobileSubscription;
    private string _errorMessage = string.Empty;
    private bool _isSent;
    private string _remainingTime = "01:00";
    private string _verificationCode;

    private Guid? VerificationSessionIdentifier { get; set; } = null;

    public VerifyOtpViewModel(
        ISystemEvents systemEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen, string mobileNumber) : base(systemEvents, networkProvider, localizationService)
    {
        _mobileNumber = mobileNumber;
        _verificationCode = string.Empty;

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

        _mobileSubscription = MessageBus.Current.Listen<string>("Mobile")
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(mobile => Task.Run(async () => await ValidatePhoneNumber(mobile)));
    }

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

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }

    public ReactiveCommand<Unit, Unit> NavToPasswordConfirmation { get; }

    public ViewModelActivator Activator { get; } = new();

    private ValidatePhoneNumberResponse _validatePhoneNumberResponse;

    private ulong _secondsRemaining;

    public ulong SecondsRemaining
    {
        get => _secondsRemaining;
        private set
        {
            this.RaiseAndSetIfChanged(ref _secondsRemaining, value);
            RemainingTime = FormatRemainingTime(value);
        }
    }

    private async Task ValidatePhoneNumber(string phoneNumber)
    {
        using CancellationTokenSource cancellationTokenSource = new();
        string? systemDeviceIdentifier = SystemDeviceIdentifier();

        if (Guid.TryParse(phoneNumber, out Guid parsedGuid))
        {
            return;
        }

        ValidatePhoneNumberRequest request = new()
        {
            PhoneNumber = phoneNumber, AppDeviceIdentifier = ByteString.CopyFrom(systemDeviceIdentifier, Encoding.UTF8),
        };

        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
        _ = await NetworkProvider.ExecuteServiceRequestAsync(
            connectId,
            RcpServiceType.ValidatePhoneNumber,
            request.ToByteArray(),
            ServiceFlowType.Single,
            payload =>
            {
                _validatePhoneNumberResponse = Helpers.ParseFromBytes<ValidatePhoneNumberResponse>(payload);
                if (_validatePhoneNumberResponse.Result == VerificationResult.InvalidPhone)
                {
                    ErrorMessage = _validatePhoneNumberResponse.Message;
                }
                else
                {
                    Task.Run(async () => await InitiateVerification(_validatePhoneNumberResponse.PhoneNumberIdentifier,
                        InitiateVerificationRequest.Types.Type.SendOtp), cancellationTokenSource.Token);
                }

                return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value));
            },
            cancellationTokenSource.Token
        );
    }

    private async Task InitiateVerification(ByteString phoneNumberIdentifier,
        InitiateVerificationRequest.Types.Type type)
    {
        using CancellationTokenSource cancellationTokenSource = new();

        string? systemDeviceIdentifier = SystemDeviceIdentifier();

        InitiateVerificationRequest membershipVerificationRequest = new()
        {
            PhoneNumberIdentifier = phoneNumberIdentifier,
            AppDeviceIdentifier = ByteString.CopyFrom(systemDeviceIdentifier, Encoding.UTF8),
            Purpose = VerificationPurpose.Registration,
            Type = type
        };

        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
        _ = await NetworkProvider.ExecuteServiceRequestAsync(
            connectId,
            RcpServiceType.InitiateVerification,
            membershipVerificationRequest.ToByteArray(),
            ServiceFlowType.ReceiveStream,
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
            },
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
            AppDeviceIdentifier = ByteString.CopyFrom(systemDeviceIdentifier, Encoding.UTF8)
        };

        await NetworkProvider.ExecuteServiceRequestAsync(
            ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
            RcpServiceType.VerifyOtp,
            verifyCodeRequest.ToByteArray(),
            ServiceFlowType.Single,
            payload =>
            {
                VerifyCodeResponse verifyCodeReply = Helpers.ParseFromBytes<VerifyCodeResponse>(payload);
                if (verifyCodeReply.Result == VerificationResult.Succeeded)
                {
                    Membership membership = verifyCodeReply.Membership;
                    /*MessageBus.Current.SendMessage(
                        new VerifyCodeNavigateToView(
                            Helpers.FromByteStringToGuid(membership.UniqueIdentifier).ToString(),
                            MembershipViewType.ConfirmPassword),
                        "VerifyCodeNavigateToView");*/
                }
                else if (verifyCodeReply.Result == VerificationResult.InvalidOtp)
                {
                }

                return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value));
            },
            CancellationToken.None
        );
    }

    private void ReSendVerificationCode()
    {
        Task.Run(async () => await InitiateVerification(_validatePhoneNumberResponse.PhoneNumberIdentifier,
            InitiateVerificationRequest.Types.Type.ResendOtp));
    }

    private string FormatRemainingTime(ulong seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        string t = time.ToString(@"mm\:ss");
        Console.WriteLine(t);
        return t;
    }

    public string? UrlPathSegment { get; } = "/verification-code-entry";

    public IScreen HostScreen { get; }
}