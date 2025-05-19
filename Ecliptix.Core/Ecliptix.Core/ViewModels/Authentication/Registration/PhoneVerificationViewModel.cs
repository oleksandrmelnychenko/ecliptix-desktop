using System;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public record VerifyCodeNavigateToView(string Mobile, AuthViewType ViewTypeToNav);

public class PhoneVerificationViewModel : ViewModelBase
{
    private static readonly Regex InternationalPhoneNumberRegex =
        new(@"^\+(?:[0-9] ?){6,14}[0-9]$", RegexOptions.Compiled);

    private string _errorMessage = string.Empty;
    private string _mobile = "+380970177443";

    public PhoneVerificationViewModel()
    {
        IObservable<bool> isMobileValid = this.WhenAnyValue(x => x.Mobile)
            .Select(ValidateMobileNumber)
            .StartWith(ValidateMobileNumber(Mobile));

        VerifyMobileCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ErrorMessage = string.Empty;
            MessageBus.Current.SendMessage(new VerifyCodeNavigateToView(Mobile, AuthViewType.VerificationCodeEntry),
                "VerifyCodeNavigateToView");
        });

        isMobileValid
            .Select(isValid =>
                isValid
                    ? string.Empty
                    : "Invalid format. Use +countrycode followed by number (e.g., +12025550123).")
            .Subscribe(error => ErrorMessage = error);

        ResendCodeCommand = ReactiveCommand.Create(() => { Console.WriteLine("Resend code requested."); },
            Observable.Return(true));
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

    public ReactiveCommand<Unit, Unit> VerifyMobileCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendCodeCommand { get; }

    private static bool ValidateMobileNumber(string? mobile)
    {
        if (string.IsNullOrWhiteSpace(mobile)) return false;

        return InternationalPhoneNumberRegex.IsMatch(mobile);
    }
}