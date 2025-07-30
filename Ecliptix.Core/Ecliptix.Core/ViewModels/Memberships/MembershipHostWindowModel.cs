using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Memberships;

public class MembershipHostWindowModel : ViewModelBase, IScreen
{
    private bool _isConnected = true;
    private bool _canNavigateBack;
    private readonly IBottomSheetEvents _bottomSheetEvents;
    private readonly ISecureStorageProvider _secureStorageProvider;
    private readonly IDisposable _connectivitySubscription;

    private static readonly IReadOnlyDictionary<string, string> SupportedCountries = new Dictionary<string, string>
    {
        { "UA", "uk-UA" },
        { "US", "en-US" },
    }.AsReadOnly();

    public RoutingState Router { get; } = new();

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

    public LanguageSwitcherViewModel LanguageSwitcher { get; }

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenSupportCommand { get; }

    public ReactiveCommand<Unit, Unit> CheckCountryCultureMismatchCommand { get; }

    public MembershipHostWindowModel(
        IBottomSheetEvents bottomSheetEvents,
        ISystemEvents systemEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        InternetConnectivityObserver connectivityObserver,
        ISecureStorageProvider secureStorageProvider,
        IAuthenticationService authenticationService)
        : base(systemEvents, networkProvider, localizationService)
    {
        _bottomSheetEvents = bottomSheetEvents ?? throw new ArgumentNullException(nameof(bottomSheetEvents));
        _secureStorageProvider = secureStorageProvider;

        LanguageSwitcher = new LanguageSwitcherViewModel(localizationService, secureStorageProvider);
        _connectivitySubscription = connectivityObserver.Subscribe(status => { IsConnected = status; });

        Navigate = ReactiveCommand.CreateFromObservable<MembershipViewType, IRoutableViewModel>(viewType =>
            Router.Navigate.Execute(
                CreateViewModelForView(systemEvents, viewType, networkProvider, localizationService,
                    authenticationService)
            ));

        CheckCountryCultureMismatchCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await CheckCountryCultureMismatchAsync();
            return Unit.Default;
        });

        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/privacy"));
        OpenTermsOfServiceCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/terms"));
        OpenSupportCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/support"));

        this.WhenAnyObservable(x => x.Router.NavigateBack.CanExecute)
            .Subscribe(canExecute => { CanNavigateBack = canExecute; });

        this.WhenActivated(disposables =>
        {
            Observable.Timer(TimeSpan.FromSeconds(2), RxApp.MainThreadScheduler)
                .SelectMany(_ => CheckCountryCultureMismatchCommand.Execute())
                .Subscribe(_ => { })
                .DisposeWith(disposables);

            Navigate.Execute(MembershipViewType.MembershipWelcome)
                .Subscribe()
                .DisposeWith(disposables);
        });
    }

    private async Task CheckCountryCultureMismatchAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> appSettings =
            await _secureStorageProvider.GetApplicationInstanceSettingsAsync();

        if (appSettings.IsOk)
        {
            ApplicationInstanceSettings applicationInstanceSettings = appSettings.Unwrap();
            if (!string.IsNullOrEmpty(applicationInstanceSettings.Country))
            {
                string currentCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;

                string expectedCulture =
                    SupportedCountries.GetValueOrDefault(applicationInstanceSettings.Country, "en-US");

                if (!string.Equals(currentCulture, expectedCulture, StringComparison.OrdinalIgnoreCase))
                {
                    _bottomSheetEvents.BottomSheetChangedState(
                        BottomSheetChangedEvent.New(BottomSheetComponentType.DetectedLocalization));
                }
            }
        }
    }

    private static void OpenUrl(string url)
    {
        Log.Information($"Opening URL: {url}");
    }

    private IRoutableViewModel CreateViewModelForView(
        ISystemEvents systemEvents,
        MembershipViewType viewType,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IAuthenticationService authenticationService)
    {
        return viewType switch
        {
            MembershipViewType.SignIn => new SignInViewModel(systemEvents, networkProvider, localizationService,
                authenticationService, this),
            MembershipViewType.MembershipWelcome => new WelcomeViewModel(this, systemEvents, localizationService,
                networkProvider),
            MembershipViewType.PhoneVerification => new MobileVerificationViewModel(systemEvents, networkProvider,
                localizationService, this),
            MembershipViewType.VerificationCodeEntry => new VerificationCodeEntryViewModel(systemEvents,
                networkProvider, localizationService, this),
            MembershipViewType.ConfirmPassword => new PasswordConfirmationViewModel(systemEvents, networkProvider,
                localizationService, this),
            MembershipViewType.PassPhase => new PassPhaseViewModel(systemEvents, localizationService, this,
                networkProvider),
            _ => throw new ArgumentOutOfRangeException(nameof(viewType))
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectivitySubscription.Dispose();
        }

        base.Dispose(disposing);
    }
}