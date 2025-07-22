using System;
using System.Diagnostics;
using System.Reactive;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.ViewModels.Memberships.SignIn;
using Ecliptix.Core.ViewModels.Memberships.SignUp;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Authentication;

public class MembershipHostWindowModel : ReactiveObject, IScreen
{
    public RoutingState Router { get; } = new();

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    private readonly IDisposable _connectivitySubscription;
    private bool _isConnected;

    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }

    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSupportCommand { get; }

    public MembershipHostWindowModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        InternetConnectivityObserver connectivityObserver
    )
    {
        _connectivitySubscription = connectivityObserver.Subscribe(status => { IsConnected = status; });

        Navigate = ReactiveCommand.CreateFromObservable<MembershipViewType, IRoutableViewModel>(viewType =>
            Router.Navigate.Execute(
                CreateViewModelForView(viewType, networkProvider, localizationService)!
            ));

        Navigate.Execute(MembershipViewType.MembershipWelcome).Subscribe();

        this.WhenAnyObservable(x => x.Router.NavigateBack.CanExecute)
            .Subscribe(canExecute => { CanNavigateBack = canExecute; });

        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => { OpenUrl("https://ecliptix.com/privacy"); });

        OpenTermsOfServiceCommand = ReactiveCommand.Create(() => { OpenUrl("https://ecliptix.com/terms"); });

        OpenSupportCommand = ReactiveCommand.Create(() => { OpenUrl("https://ecliptix.com/support"); });
    }

    private bool _canNavigateBack;

    public bool CanNavigateBack
    {
        get => _canNavigateBack;
        private set => this.RaiseAndSetIfChanged(ref _canNavigateBack, value);
    }

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Handle URL opening failure silently
        }
    }

    private IRoutableViewModel CreateViewModelForView(
        MembershipViewType viewType,
        NetworkProvider networkProvider,
        ILocalizationService localizationService
    )
    {
        return viewType switch
        {
            MembershipViewType.SignIn => new SignInViewModel(networkProvider, localizationService, this),
            MembershipViewType.MembershipWelcome => new WelcomeViewModel(this),
            MembershipViewType.PhoneVerification => new MobileVerificationViewModel(networkProvider, localizationService,
                this),
            MembershipViewType.VerificationCodeEntry => new VerificationCodeEntryViewModel(networkProvider,
                localizationService, this),
            MembershipViewType.ConfirmPassword => new PasswordConfirmationViewModel(networkProvider,
                localizationService, this),
            _ => throw new ArgumentOutOfRangeException(nameof(viewType)),
        };
    }
}