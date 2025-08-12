using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Contracts.Transport;
using Ecliptix.Core.Network.Core.Connectivity;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Core.ViewModels.Memberships.SignIn;
using Ecliptix.Core.ViewModels.Memberships.SignUp;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using ReactiveUI;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.ViewModels.Memberships;

public class MembershipHostWindowModel : ViewModelBase, IScreen
{
    private bool _canNavigateBack;
    private readonly IBottomSheetEvents _bottomSheetEvents;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IDisposable _connectivitySubscription;
    private readonly INetworkEvents _networkEvents;
    private readonly NetworkProvider _networkProvider;

    private static readonly IReadOnlyDictionary<string, string> SupportedCountries = new Dictionary<string, string>
    {
        { "UA", "uk-UA" },
        { "US", "en-US" },
    }.AsReadOnly();

    public RoutingState Router { get; } = new();


    public bool CanNavigateBack
    {
        get => _canNavigateBack;
        private set => this.RaiseAndSetIfChanged(ref _canNavigateBack, value);
    }

    public LanguageSelectorViewModel LanguageSelector { get; }

    public NetworkStatusNotificationViewModel NetworkStatusNotification { get; }

    public string AppVersion { get; }
    
    public string BuildInfo { get; }
    
    public string FullVersionInfo { get; }

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenSupportCommand { get; }

    public ReactiveCommand<Unit, Unit> CheckCountryCultureMismatchCommand { get; }

    public MembershipHostWindowModel(
        IBottomSheetEvents bottomSheetEvents,
        ISystemEvents systemEvents,
        INetworkEvents networkEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        InternetConnectivityObserver connectivityObserver,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider,
        IAuthenticationService authenticationService)
        : base(systemEvents, networkProvider, localizationService)
    {
        _networkEvents = networkEvents;
        _networkProvider = networkProvider;
        _bottomSheetEvents = bottomSheetEvents;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;

        LanguageSelector =
            new LanguageSelectorViewModel(localizationService, applicationSecureStorageProvider, rpcMetaDataProvider);
        NetworkStatusNotification = new NetworkStatusNotificationViewModel(localizationService, networkEvents);
        
        // Initialize version information
        AppVersion = VersionHelper.GetApplicationVersion();
        var buildInfo = VersionHelper.GetBuildInfo();
        BuildInfo = buildInfo?.BuildNumber ?? "dev";
        FullVersionInfo = VersionHelper.GetDisplayVersion();

        _connectivitySubscription = connectivityObserver.Subscribe(status =>
        {
            Log.Information("Network status changed to: {Status}", status);
            
            if (status)
            {
                _networkEvents.InitiateChangeState(NetworkStatusChangedEvent.New(NetworkStatus.DataCenterConnecting));
            }
            else
            {
                _networkEvents.InitiateChangeState(NetworkStatusChangedEvent.New(NetworkStatus.DataCenterDisconnected));
            }
        });
        
        Navigate = ReactiveCommand.CreateFromObservable<MembershipViewType, IRoutableViewModel>(viewType =>
            Router.Navigate.Execute(
                CreateViewModelForView(systemEvents, viewType, networkProvider, localizationService,
                    authenticationService, applicationSecureStorageProvider)
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
            
            _networkEvents.ManualRetryRequested
                .SelectMany(async e =>
                {
                    Result<Utilities.Unit, NetworkFailure> recoveryResult = 
                        await _networkProvider.ForceFreshConnectionAsync();
            
                    if (recoveryResult.IsOk)
                    {
                        _networkEvents.InitiateChangeState(
                            NetworkStatusChangedEvent.New(NetworkStatus.DataCenterConnected));
                    }
                    else
                    {
                        Log.Warning("Manual retry failed: {Error}", recoveryResult.UnwrapErr().Message);
                    }
                    
                    return Unit.Default;
                })
                .Subscribe()
                .DisposeWith(disposables);
            Observable.Timer(TimeSpan.FromSeconds(2), RxApp.MainThreadScheduler)
                .SelectMany(_ => CheckCountryCultureMismatchCommand.Execute())
                .Subscribe(_ => { })
                .DisposeWith(disposables);
                //
            Navigate.Execute(MembershipViewType.Welcome)
                .Subscribe()
                .DisposeWith(disposables);
        });
    }

    //TODO: this method must be updated and reworked.
    private async Task CheckCountryCultureMismatchAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> appSettings =
            await _applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync();

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
        Log.Information("Opening URL: {Url}", url);
    }

    private IRoutableViewModel CreateViewModelForView(
        ISystemEvents systemEvents,
        MembershipViewType viewType,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IAuthenticationService authenticationService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider)
    {
        return viewType switch
        {
            MembershipViewType.SignIn => new SignInViewModel(systemEvents, networkProvider, localizationService,
                authenticationService, this),
            MembershipViewType.Welcome => new WelcomeViewModel(this, systemEvents, localizationService,
                networkProvider),
            MembershipViewType.MobileVerification => new MobileVerificationViewModel(systemEvents, networkProvider,
                localizationService, this, applicationSecureStorageProvider),
            MembershipViewType.ConfirmSecureKey => new PasswordConfirmationViewModel(systemEvents, networkProvider,
                localizationService, this, applicationSecureStorageProvider),
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