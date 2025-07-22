using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Memberships.SignUp;

public class MobileVerificationViewModel : ViewModelBase, IActivatableViewModel, IRoutableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    private string _errorMessage = string.Empty;
    private string _mobileNumber = "+380970177443";

    private readonly NetworkProvider _networkProvider;
    private readonly ILocalizationService _localizationService;
    public ILocalizationService LocalizationService => _localizationService;

    public string? UrlPathSegment { get; } = "/mobile-verification";

    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, Unit> NavToVerifyOtp { get; set; }
    
    public string InvalidFormatError =>
        _localizationService["Authentication.Registration.phoneVerification.error.invalidFormat"];

    public MobileVerificationViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen)
    {
        _networkProvider = networkProvider;
        _localizationService = localizationService;
        HostScreen = hostScreen;

        NavToVerifyOtp = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.VerificationCodeEntry);
        });

        VerifyMobileCommand = ReactiveCommand.CreateFromTask(async () => { ErrorMessage = string.Empty; });

        ResendCodeCommand = ReactiveCommand.Create(() => { Console.WriteLine("Resend code requested."); },
            Observable.Return(true));

        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handler => _localizationService.LanguageChanged += handler,
                    handler => _localizationService.LanguageChanged -= handler
                )
                .Subscribe(_ => { this.RaisePropertyChanged(nameof(InvalidFormatError)); })
                .DisposeWith(disposables);
        });
    }

    public string MobileNumber
    {
        get => _mobileNumber;
        set => this.RaiseAndSetIfChanged(ref _mobileNumber, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public ReactiveCommand<Unit, Unit> VerifyMobileCommand { get; }
    public ReactiveCommand<Unit, Unit> ResendCodeCommand { get; }
}