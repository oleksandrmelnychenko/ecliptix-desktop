using System;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Ecliptix.Core.Controls.LanguageSwitcher;
using Ecliptix.Core.Controls.Modals.BottomSheetModal;
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
    
    private BottomSheetViewModel _bottomSheetViewModel;

    public BottomSheetViewModel BottomSheetViewModel
    {
        get => _bottomSheetViewModel;
        set => this.RaiseAndSetIfChanged(ref _bottomSheetViewModel, value);
    }

    public ReactiveCommand<Unit, Unit> OpenBottomSheetCommand { get; }
    
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
        
        BottomSheetViewModel = new BottomSheetViewModel(networkProvider, localizationService);

        OpenBottomSheetCommand = ReactiveCommand.Create(ShowSimpleBottomSheet);
        
    }
    
    private void ShowSimpleBottomSheet()
    {
        BottomSheetViewModel.ClearContent();
        
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "Welcome to the bottom sheet!",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        });
            
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "This is a simple example with some text content.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 15)
        });
        
        var closeButton = new Button 
        { 
            Content = "Close",
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Avalonia.Thickness(20, 8),
            Command = BottomSheetViewModel.HideCommand
        };
        
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "Welcome to the bottom sheet!",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        });
            
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "This is a simple example with some text content.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 15)
        });
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "Welcome to the bottom sheet!",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        });
            
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "This is a simple example with some text content.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 15)
        });
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "Welcome to the bottom sheet!",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        });
            
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "This is a simple example with some text content.",
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(0, 0, 0, 15)
        });
        BottomSheetViewModel.AddContent(new TextBlock 
        { 
            Text = "Welcome to the bottom sheet!",
            FontSize = 16,
            FontWeight = FontWeight.SemiBold,
            Margin = new Avalonia.Thickness(0, 0, 0, 10)
        });
        BottomSheetViewModel.AddContent(closeButton);
        BottomSheetViewModel.ShowCommand.Execute().Subscribe();
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