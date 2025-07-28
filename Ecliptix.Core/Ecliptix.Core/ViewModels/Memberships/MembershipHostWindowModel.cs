using System;
using System.Reactive;
using Ecliptix.Core.Controls.LanguageSwitcher;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Core.ViewModels.Memberships.SignIn;
using Ecliptix.Core.ViewModels.Memberships.SignUp;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships;

public class MembershipHostWindowModel : ViewModelBase, IScreen
{
    public RoutingState Router { get; } = new();
    
    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    private readonly IDisposable _connectivitySubscription;
    private bool _isConnected = true;
    private bool _canNavigateBack;
    
    public bool IsConnected
    {
        get => _isConnected;
        set => this.RaiseAndSetIfChanged(ref _isConnected, value);
    }
    
    public bool CanNavigateBack
    {
        get => _canNavigateBack;
        private set => this.RaiseAndSetIfChanged(ref _canNavigateBack, value);
    }
    
    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; }
    public ReactiveCommand<Unit, Unit> OpenSupportCommand { get; }

  
    public LanguageSwitcherViewModel LanguageSwitcher { get; }
    
    
    // For a test purposes 
    private int _modalZIndex = 0;
    private double _modalOpacity = 0;
    public int ModalZIndex
    {
        get => _modalZIndex;
        set => this.RaiseAndSetIfChanged(ref _modalZIndex, value);
    }

    public double ModalOpacity
    {
        get => _modalOpacity;
        set => this.RaiseAndSetIfChanged(ref _modalOpacity, value);
    }
    public ReactiveCommand<Unit, Unit> ToggleModal { get; }
    // For a test purposes 
    
    public MembershipHostWindowModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        InternetConnectivityObserver connectivityObserver,
        ISecureStorageProvider secureStorageProvider
    ) : base(networkProvider,localizationService)
    {
        _connectivitySubscription = connectivityObserver.Subscribe(async status =>
        {
            IsConnected = status;
        });

        LanguageSwitcher = new LanguageSwitcherViewModel(localizationService, secureStorageProvider);

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
        
        ToggleModal = ReactiveCommand.Create(() =>
        {
            if (ModalZIndex == 10)
            {
                ModalZIndex = 0;
                ModalOpacity = 0;
            }
            else
            {
                ModalZIndex = 10;
                ModalOpacity = 1;
            }
        });
        
    }


    private static void OpenUrl(string url)
    {
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
            MembershipViewType.MembershipWelcome => new WelcomeViewModel(this, localizationService, networkProvider),
            MembershipViewType.PhoneVerification => new MobileVerificationViewModel(networkProvider,
                localizationService,
                this),
            MembershipViewType.VerificationCodeEntry => new VerificationCodeEntryViewModel(networkProvider,
                localizationService, this),
            MembershipViewType.ConfirmPassword => new PasswordConfirmationViewModel(networkProvider,
                localizationService, this),
            MembershipViewType.PassPhase => new PassPhaseViewModel(localizationService, this, networkProvider),
            _ => throw new ArgumentOutOfRangeException(nameof(viewType)),
        };
    }
}