using System;
using System.Linq;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protobuf.Verification;
using Google.Protobuf;
using Grpc.Core;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Core.Protocol.Utilities.Unit;

namespace Ecliptix.Core.ViewModels.Memberships;

public class VerifyMobileViewModel : ViewModelBase
{
    private static readonly Regex InternationalPhoneNumberRegex =
        new(@"^\+(?:[0-9] ?){6,14}[0-9]$", RegexOptions.Compiled);

    private string _errorMessage = string.Empty;
    private bool _isVerifying;
    private string _mobile = "+380970177443";
    private string _verificationStatus = string.Empty;
    private string _remainingTime = "01:00"; // Initial value

    public string RemainingTime
    {
        get => _remainingTime;
        private set => this.RaiseAndSetIfChanged(ref _remainingTime, value);
    }
    
    public VerifyMobileViewModel(NetworkController networkController)
    {
        IObservable<bool> isMobileValid = this.WhenAnyValue(x => x.Mobile)
            .Select(ValidateMobileNumber)
            .StartWith(ValidateMobileNumber(Mobile));

        VerifyMobileCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                IsVerifying = true;
                ErrorMessage = string.Empty;
                VerificationStatus = "Checking for existing verification session...";

                CancellationTokenSource cancellationToken = new();

                Guid? systemAppDeviceId = SystemAppDeviceId();

                MembershipVerificationRequest membershipVerificationRequest = new()
                {
                    Mobile = Mobile,
                    UniqueAppDeviceRec = Network.Utilities.GuidToByteString(systemAppDeviceId!.Value),
                    VerificationType = VerificationType.Signup
                };

                uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
                await networkController.ExecuteServiceAction(
                    connectId, RcpServiceAction.GetVerificationSessionIfExist,
                    membershipVerificationRequest.ToByteArray(),
                    ServiceFlowType.ReceiveStream,
                    payload =>
                    {
                        TimerTick timerTick = Network.Utilities.ParseFromBytes<TimerTick>(payload);
                        RemainingTime = FormatRemainingTime(timerTick.RemainingSeconds);
                        return Task.FromResult(Result<ShieldUnit, ShieldFailure>.Ok(ShieldUnit.Value));
                    }, cancellationToken.Token
                );
            },
            isMobileValid);

        // Update ErrorMessage reactively based on Mobile validity
        isMobileValid
            .Where(_ => !IsVerifying)
            .Select(isValid =>
                isValid
                    ? string.Empty
                    : "Invalid format. Use +countrycode followed by number (e.g., +12025550123).")
            .Subscribe(error => ErrorMessage = error);

        // ResendCodeCommand (unchanged)
        ResendCodeCommand = ReactiveCommand.Create(() => { Console.WriteLine("Resend code requested."); },
            Observable.Return(true));
    }
    
    private string FormatRemainingTime(ulong seconds)
    {
        TimeSpan time = TimeSpan.FromSeconds(seconds);
        return time.ToString(@"mm\:ss");
    }

    public string Mobile
    {
        get => _mobile;
        set => this.RaiseAndSetIfChanged(ref _mobile, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public bool IsVerifying
    {
        get => _isVerifying;
        private set => this.RaiseAndSetIfChanged(ref _isVerifying, value);
    }

    public string VerificationStatus
    {
        get => _verificationStatus;
        private set => this.RaiseAndSetIfChanged(ref _verificationStatus, value);
    }

    public ReactiveCommand<Unit, Unit> VerifyMobileCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendCodeCommand { get; }

    private static bool ValidateMobileNumber(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return false;

        return InternationalPhoneNumberRegex.IsMatch(mobile);
    }

    private string GetSanitizedMobile()
    {
        if (string.IsNullOrWhiteSpace(Mobile)) return string.Empty;

        return "+" + new string(Mobile.Where(char.IsDigit).ToArray());
    }
}