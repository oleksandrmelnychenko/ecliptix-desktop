using System;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Controls.LanguageSwitcher;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Core.ViewModels.Memberships.SignIn;
using Ecliptix.Core.ViewModels.Memberships.SignUp;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Utilities;
using ReactiveUI;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Memberships;

public class MembershipHostWindowModel : ViewModelBase, IScreen
{
    private readonly IBottomSheetEvents _bottomSheetEvents;
    public RoutingState Router { get; } = new();

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    private readonly IDisposable _connectivitySubscription;
    private bool _isConnected = true;
    private bool _canNavigateBack;

    private ApplicationInstanceSettings _applicationInstanceSettings;

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

    public MembershipHostWindowModel(
        IBottomSheetEvents bottomSheetEvents,
        ISystemEvents systemEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        InternetConnectivityObserver connectivityObserver,
        ISecureStorageProvider secureStorageProvider,
        IAuthenticationService authenticationService
    ) : base(systemEvents, networkProvider, localizationService)
    {
        _bottomSheetEvents = bottomSheetEvents;
        _connectivitySubscription = connectivityObserver.Subscribe(async status => { IsConnected = status; });

        LanguageSwitcher = new LanguageSwitcherViewModel(localizationService, secureStorageProvider);

        _ = Task.Run(async () =>
        {
            Result<ApplicationInstanceSettings, InternalServiceApiFailure> appSettings =
                await secureStorageProvider.GetApplicationInstanceSettingsAsync();

            if (appSettings.IsOk)
            {
                ApplicationInstanceSettings applicationInstanceSettings = appSettings.Unwrap();
                string country = applicationInstanceSettings.Country;
                if (!string.IsNullOrEmpty(country))
                {
                    string expectedCulture = country switch
                    {
                        "UA" => "uk-UA",
                        _ => "en-US" 
                    };
                    
                    
                }
            }
        });

        Navigate = ReactiveCommand.CreateFromObservable<MembershipViewType, IRoutableViewModel>(viewType =>
            Router.Navigate.Execute(
                CreateViewModelForView(systemEvents, viewType, networkProvider, localizationService,
                    authenticationService)
            ));

        Navigate.Execute(MembershipViewType.MembershipWelcome).Subscribe();

        this.WhenAnyObservable(x => x.Router.NavigateBack.CanExecute)
            .Subscribe(canExecute => { CanNavigateBack = canExecute; });

        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => { OpenUrl("https://ecliptix.com/privacy"); });

        OpenTermsOfServiceCommand = ReactiveCommand.Create(() => { OpenUrl("https://ecliptix.com/terms"); });

        OpenSupportCommand = ReactiveCommand.Create(() => { OpenUrl("https://ecliptix.com/support"); });
    }

    private void ShowSimpleBottomSheet()
    {
        _bottomSheetEvents.BottomSheetChangedState(
            BottomSheetChangedEvent.New(BottomSheetComponentType.DetectedLocalization));
    }

    private static void OpenUrl(string url)
    {
    }

    private IRoutableViewModel CreateViewModelForView(
        ISystemEvents systemEvents,
        MembershipViewType viewType,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IAuthenticationService authenticationService
    )
    {
        return viewType switch
        {
            MembershipViewType.SignIn => new SignInViewModel(systemEvents, networkProvider, localizationService,
                authenticationService, this),
            MembershipViewType.MembershipWelcome => new WelcomeViewModel(this, systemEvents, localizationService,
                networkProvider),
            MembershipViewType.PhoneVerification => new MobileVerificationViewModel(systemEvents, networkProvider,
                localizationService,
                this),
            MembershipViewType.VerificationCodeEntry => new VerificationCodeEntryViewModel(systemEvents,
                networkProvider,
                localizationService, this),
            MembershipViewType.ConfirmPassword => new PasswordConfirmationViewModel(systemEvents, networkProvider,
                localizationService, this),
            MembershipViewType.PassPhase => new PassPhaseViewModel(systemEvents, localizationService, this,
                networkProvider),
            _ => throw new ArgumentOutOfRangeException(nameof(viewType)),
        };
    }
}