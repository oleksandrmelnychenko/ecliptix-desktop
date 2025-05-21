using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text.RegularExpressions;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Authentication.Registration;

public record VerifyCodeNavigateToView(string Mobile, AuthViewType ViewTypeToNav);

public class PhoneVerificationViewModel : ViewModelBase, IActivatableViewModel
{
    public ViewModelActivator Activator { get; } = new();
    
    private static readonly Regex InternationalPhoneNumberRegex =
        new(@"^\+(?:[0-9] ?){6,14}[0-9]$", RegexOptions.Compiled);

    private string _errorMessage = string.Empty;
    private string _mobile = "+380970177443";

    private readonly ILocalizationService _localizationService;
    
    public string Title => _localizationService["Authentication.Registration.phoneVerification.title"];
    public string Description => _localizationService["Authentication.Registration.phoneVerification.description"];
    public string Hint => _localizationService["Authentication.Registration.phoneVerification.hint"];
    public string ButtonContent => _localizationService["Authentication.Registration.phoneVerification.button"];
    public string InvalidFormatError => _localizationService["Authentication.Registration.phoneVerification.error.invalidFormat"];
    
    public PhoneVerificationViewModel(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
        
        IObservable<bool> isMobileValid = this.WhenAnyValue(x => x.Mobile)
            .Select(ValidateMobileNumber)
            .StartWith(ValidateMobileNumber(Mobile));
        
        VerifyMobileCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            ErrorMessage = string.Empty;
            MessageBus.Current.SendMessage(new VerifyCodeNavigateToView(Mobile, AuthViewType.VerificationCodeEntry),
                "VerifyCodeNavigateToView");
        });

        ResendCodeCommand = ReactiveCommand.Create(() => { Console.WriteLine("Resend code requested."); },
            Observable.Return(true));
        
        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handler => _localizationService.LanguageChanged += handler,
                    handler => _localizationService.LanguageChanged -= handler
                )
                .Subscribe(_ =>
                {
                    this.RaisePropertyChanged(nameof(Title));
                    this.RaisePropertyChanged(nameof(Description));
                    this.RaisePropertyChanged(nameof(Hint));
                    this.RaisePropertyChanged(nameof(ButtonContent));
                    this.RaisePropertyChanged(nameof(InvalidFormatError));
                })
                .DisposeWith(disposables);

            isMobileValid
                .Select(isValid => isValid ? string.Empty : InvalidFormatError)
                .Subscribe(error => ErrorMessage = error)
                .DisposeWith(disposables);
        });
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