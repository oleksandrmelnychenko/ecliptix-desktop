using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protobuf.Verification;
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Core.Protocol.Utilities.Unit;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public class VerificationCodeEntryViewModel : ViewModelBase
{
    private readonly IDisposable _mobileSubscription;
    private readonly NetworkController _networkController;
    private string _errorMessage = string.Empty;
    private bool _isSent;
    private string _remainingTime = "01:00";
    private string _verificationCode;

    public VerificationCodeEntryViewModel(NetworkController networkController)
    {
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
                SystemDeviceIdentifier = Utilities.GuidToByteString(systemDeviceIdentifier.Value),
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
                            Network.Utilities.ParseFromBytes<VerificationCountdownUpdate>(payload);
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

            VerifyCodeRequest verifyCodeRequest = new()
            {
                Code = VerificationCode,
                Purpose = VerificationPurpose.Registration,
                SystemDeviceIdentifier = Utilities.GuidToByteString(systemDeviceIdentifier.Value),
            };

            await _networkController.ExecuteServiceAction(
                ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
                RcpServiceAction.VerifyCode,
                verifyCodeRequest.ToByteArray(),
                ServiceFlowType.Single,
                payload =>
                {
                    VerifyCodeResponse verifyCodeReply = Network.Utilities.ParseFromBytes<VerifyCodeResponse>(payload);

                    if (verifyCodeReply.Result == VerificationResult.Succeeded)
                    {
                        //dispose and send to the next page.    
                    }
                    else
                    {
                        if (verifyCodeReply.Result == VerificationResult.InvalidCode)
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

    private string FormatRemainingTime(ulong seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        return time.ToString(@"mm\:ss");
    }

    protected void Dispose(bool disposing)
    {
        if (disposing) _mobileSubscription?.Dispose();
    }
}