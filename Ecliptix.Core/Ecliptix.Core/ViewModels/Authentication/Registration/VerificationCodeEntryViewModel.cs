using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protobuf.Verification;
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Core.Protocol.Utilities.Unit;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class VerificationCodeEntryViewModel : ViewModelBase, IActivatableViewModel
{
    private readonly ILocalizationService _localizationService;
    
    private readonly IDisposable _mobileSubscription;
    private readonly NetworkController _networkController;
    private string _errorMessage = string.Empty;
    private bool _isSent;
    private string _remainingTime = "01:00";
    private string _verificationCode;

    public string Title => _localizationService["Authentication.Registration.verificationCodeEntry.title"];
    public string Hint => _localizationService["Authentication.Registration.verificationCodeEntry.hint"];
    public string Expiration => _localizationService["Authentication.Registration.verificationCodeEntry.expiration"];
    public string InvalidCodeError => _localizationService["Authentication.Registration.verificationCodeEntry.error.invalidCode"];
    public string VerifyButtonContent => _localizationService["Authentication.Registration.verificationCodeEntry.button.verify"];
    public string ResendButtonContent => _localizationService["Authentication.Registration.verificationCodeEntry.button.resend"];
    
    private Guid? VerificationSessionIdentifier { get; set; } = null;

    public VerificationCodeEntryViewModel(NetworkController networkController, ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        
        _networkController = networkController ?? throw new ArgumentNullException(nameof(networkController));
        _verificationCode = string.Empty;

        IObservable<bool> canExecute = this.WhenAnyValue(x => x.VerificationCode)
            .Select(code => code?.Length == 6 && code.All(char.IsDigit));

        SendVerificationCodeCommand = ReactiveCommand.CreateFromTask(SendVerificationCode, canExecute);

        _mobileSubscription = MessageBus.Current.Listen<string>("Mobile")
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(mobile =>
                Task.Run(async () => await ValidatePhoneNumber(mobile))
            );


        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handler => _localizationService.LanguageChanged += handler,
                    handler => _localizationService.LanguageChanged -= handler
                )
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(Title));
                    this.RaisePropertyChanged(nameof(Hint));
                    this.RaisePropertyChanged(nameof(Expiration));
                    this.RaisePropertyChanged(nameof(InvalidCodeError));
                    this.RaisePropertyChanged(nameof(VerifyButtonContent));
                    this.RaisePropertyChanged(nameof(ResendButtonContent));
                })
                .DisposeWith(disposables);

            
        });
    
        ResendSendVerificationCodeCommand = ReactiveCommand.CreateFromTask(ReSendVerificationCode);
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
        private set => this.RaiseAndSetIfChanged(ref _remainingTime, value);
    }

    public ReactiveCommand<Unit, Unit> SendVerificationCodeCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendSendVerificationCodeCommand { get; }
    public ViewModelActivator Activator { get; } = new();

    private async Task ValidatePhoneNumber(string phoneNumber)
    {
        using CancellationTokenSource cancellationTokenSource = new();

        ValidatePhoneNumberRequest request = new()
        {
            PhoneNumber = phoneNumber
        };

        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
        _ = await _networkController.ExecuteServiceAction(
            connectId,
            RcpServiceAction.ValidatePhoneNumber,
            request.ToByteArray(),
            ServiceFlowType.Single,
            payload =>
            {
                try
                {
                    ValidatePhoneNumberResponse validatePhoneNumberResponse =
                        Utilities.ParseFromBytes<ValidatePhoneNumberResponse>(payload);

                    if (validatePhoneNumberResponse.Result == VerificationResult.InvalidPhone)
                    {
                        ErrorMessage = validatePhoneNumberResponse.Message;
                    }
                    else
                    {
                        Task.Run(
                            async () => await InitiateVerification(validatePhoneNumberResponse.PhoneNumberIdentifier),
                            cancellationTokenSource.Token);
                    }

                    return Task.FromResult(Result<ShieldUnit, ShieldFailure>.Ok(ShieldUnit.Value));
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"Failed to process timer tick: {ex.Message}";
                    return Task.FromResult(
                        Result<ShieldUnit, ShieldFailure>.Err(ShieldFailure.Generic(ex.Message, ex)));
                }
            },
            cancellationTokenSource.Token
        );
    }

    private async Task InitiateVerification(ByteString phoneNumberIdentifier)
    {
        using CancellationTokenSource cancellationTokenSource = new();
        try
        {
            Guid? systemDeviceIdentifier = SystemDeviceIdentifier();
            if (!systemDeviceIdentifier.HasValue)
            {
                ErrorMessage = "Invalid device ID";
                return;
            }

            InitiateVerificationRequest membershipVerificationRequest = new()
            {
                PhoneNumberIdentifier = phoneNumberIdentifier,
                AppDeviceIdentifier = Utilities.GuidToByteString(systemDeviceIdentifier.Value),
                Purpose = VerificationPurpose.Registration
            };

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
            _ = await _networkController.ExecuteServiceAction(
                connectId,
                RcpServiceAction.InitiateVerification,
                membershipVerificationRequest.ToByteArray(),
                ServiceFlowType.ReceiveStream,
                payload =>
                {
                    try
                    {
                        VerificationCountdownUpdate timerTick =
                            Utilities.ParseFromBytes<VerificationCountdownUpdate>(payload);

                        VerificationSessionIdentifier ??= Utilities.FromByteStringToGuid(timerTick.SessionIdentifier);

                        RemainingTime = FormatRemainingTime(timerTick.SecondsRemaining);
                        return Task.FromResult(Result<ShieldUnit, ShieldFailure>.Ok(ShieldUnit.Value));
                    }
                    catch (Exception ex)
                    {
                        ErrorMessage = $"Failed to process timer tick: {ex.Message}";
                        return Task.FromResult(
                            Result<ShieldUnit, ShieldFailure>.Err(ShieldFailure.Generic(ex.Message, ex)));
                    }
                },
                cancellationTokenSource.Token
            );
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Verification request failed: {ex.Message}";
        }
    }

    private async Task SendVerificationCode()
    {
        try
        {
            Guid? systemDeviceIdentifier = SystemDeviceIdentifier();
            if (!systemDeviceIdentifier.HasValue)
            {
                ErrorMessage = "Invalid device ID";
                return;
            }

            IsSent = true;
            ErrorMessage = string.Empty;

            // Dummy server validation logic
            if (VerificationCode == "123456") // Simulate a valid code
            {
                MessageBus.Current.SendMessage(
                    new VerifyCodeNavigateToView(string.Empty, AuthViewType.ConfirmPassword),
                    "VerifyCodeNavigateToView");
            }
            
            
            VerifyCodeRequest verifyCodeRequest = new()
            {
                Code = VerificationCode,
                Purpose = VerificationPurpose.Registration,
                AppDeviceIdentifier = Utilities.GuidToByteString(systemDeviceIdentifier.Value),
            };

            await _networkController.ExecuteServiceAction(
                ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
                RcpServiceAction.VerifyOtp,
                verifyCodeRequest.ToByteArray(),
                ServiceFlowType.Single,
                payload =>
                {
                    VerifyCodeResponse verifyCodeReply = Utilities.ParseFromBytes<VerifyCodeResponse>(payload);

                    if (verifyCodeReply.Result == VerificationResult.Succeeded)
                    {
                        MessageBus.Current.SendMessage(
                            new VerifyCodeNavigateToView(string.Empty, AuthViewType.ConfirmPassword),
                            "VerifyCodeNavigateToView");
                    }
                    else
                    {
                        if (verifyCodeReply.Result == VerificationResult.InvalidOtp)
                        {
                        }
                    }

                    return Task.FromResult(Result<ShieldUnit, ShieldFailure>.Ok(ShieldUnit.Value));
                },
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Failed to send verification code: {ex.Message}";
            IsSent = false;
        }
    }

    private async Task ReSendVerificationCode()
    {
        IsSent = true;
        ErrorMessage = string.Empty;

        if (!VerificationSessionIdentifier.HasValue) return;

        InitiateResendOtpRequest initiateResendVerificationRequest = new()
        {
            SessionIdentifier = Utilities.GuidToByteString(VerificationSessionIdentifier.Value)
        };

        await _networkController.ExecuteServiceAction(
            ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
            RcpServiceAction.InitiateResendVerification,
            initiateResendVerificationRequest.ToByteArray(),
            ServiceFlowType.Single,
            payload =>
            {
                ResendOtpResponse resendOtpResponse = Utilities.ParseFromBytes<ResendOtpResponse>(payload);

                if (resendOtpResponse.Result == VerificationResult.Succeeded)
                {
                }

                return Task.FromResult(Result<ShieldUnit, ShieldFailure>.Ok(ShieldUnit.Value));
            },
            CancellationToken.None
        );
    }

    private string FormatRemainingTime(ulong seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        return time.ToString(@"mm\:ss");
    }
}