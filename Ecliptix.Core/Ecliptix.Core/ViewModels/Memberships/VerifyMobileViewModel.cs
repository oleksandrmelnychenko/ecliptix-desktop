using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Ecliptix.Core.Network;
using Grpc.Core;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class VerifyMobileViewModel : ReactiveObject
{
    private string _mobile = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isVerifying;
    private string _verificationStatus = string.Empty;

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

    private static readonly Regex InternationalPhoneNumberRegex =
        new(@"^\+(?:[0-9] ?){6,14}[0-9]$", RegexOptions.Compiled);

    public VerifyMobileViewModel(NetworkController networkController)
    {

        IObservable<bool> isMobileValid = this.WhenAnyValue(x => x.Mobile)
            .Select(ValidateMobileNumber)
            .StartWith(ValidateMobileNumber(Mobile));

        VerifyMobileCommand = ReactiveCommand.CreateFromTask(
             () =>
            {
                IsVerifying = true;
                ErrorMessage = string.Empty;
                VerificationStatus = "Checking for existing verification session...";

                try
                {
                    
                }
                catch (RpcException ex)
                {
                    ErrorMessage = $"Verification failed: {ex.Status.Detail}";
                    Console.Error.WriteLine($"gRPC error: {ex.Message}");
                    throw;
                }
                catch (Exception ex)
                {
                    ErrorMessage = $"An unexpected error occurred: {ex.Message}";
                    Console.Error.WriteLine($"Unexpected error: {ex.Message}");
                    throw;
                }
                finally
                {
                    IsVerifying = false;
                }

                return null;
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

    private static bool ValidateMobileNumber(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile))
        {
            return false;
        }

        return InternationalPhoneNumberRegex.IsMatch(mobile);
    }

    private string GetSanitizedMobile()
    {
        if (string.IsNullOrWhiteSpace(Mobile)) return string.Empty;

        return "+" + new string(Mobile.Where(char.IsDigit).ToArray());
    }
}