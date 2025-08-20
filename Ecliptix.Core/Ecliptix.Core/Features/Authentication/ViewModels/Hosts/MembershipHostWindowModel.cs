using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.BottomSheet;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Configuration;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals.BottomSheetModal.Components;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Network.Core.Connectivity;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Welcome;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.SignIn;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using ReactiveUI;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Hosts;

public class MembershipHostWindowModel : Core.MVVM.ViewModelBase, IScreen, IDisposable
{
    private bool _canNavigateBack;
    private readonly IBottomSheetEvents _bottomSheetEvents;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IDisposable _connectivitySubscription;
    private readonly INetworkEvents _networkEvents;
    private readonly NetworkProvider _networkProvider;

    private readonly ISystemEvents _systemEvents;
    private readonly IAuthenticationService _authenticationService;
    private readonly Dictionary<MembershipViewType, WeakReference<IRoutableViewModel>> _viewModelCache = new();
    private readonly CompositeDisposable _disposables = new();

    private static readonly LanguageConfiguration LanguageConfig = LanguageConfiguration.Default;

    private static readonly FrozenDictionary<MembershipViewType, Func<ISystemEvents, INetworkEvents, NetworkProvider,
        ILocalizationService, IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel,
        IRoutableViewModel>> ViewModelFactories =
        new Dictionary<MembershipViewType, Func<ISystemEvents, INetworkEvents, NetworkProvider, ILocalizationService,
            IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel, IRoutableViewModel>>
        {
            [MembershipViewType.SignIn] = (sys, netEvents, netProvider, loc, auth, storage, host) =>
                new SignInViewModel(sys, netEvents, netProvider, loc, auth, host),
            [MembershipViewType.Welcome] =
                (sys, netEvents, netProvider, loc, auth, storage, host) => new WelcomeViewModel(host, sys, loc, netProvider),
            [MembershipViewType.MobileVerification] = (sys, netEvents, netProvider, loc, auth, storage, host) =>
                new MobileVerificationViewModel(sys, netProvider, loc, host, storage),
            [MembershipViewType.ConfirmSecureKey] = (sys, netEvents, netProvider, loc, auth, storage, host) =>
                new PasswordConfirmationViewModel(sys, netProvider, loc, host, storage),
            [MembershipViewType.PassPhase] = (sys, netEvents, netProvider, loc, auth, storage, host) =>
                new PassPhaseViewModel(sys, loc, host, netProvider)
        }.ToFrozenDictionary();

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
        IAuthenticationService authenticationService,
        NetworkStatusNotificationViewModel networkStatusNotification)
        : base(systemEvents, networkProvider, localizationService)
    {
        _networkEvents = networkEvents;
        _bottomSheetEvents = bottomSheetEvents;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _systemEvents = systemEvents;
        _networkProvider = networkProvider;
        _authenticationService = authenticationService;

        LanguageSelector =
            new LanguageSelectorViewModel(localizationService, applicationSecureStorageProvider, rpcMetaDataProvider);
        NetworkStatusNotification = networkStatusNotification;

        _disposables.Add(NetworkStatusNotification);

        AppVersion = VersionHelper.GetApplicationVersion();
        BuildInfo? buildInfo = VersionHelper.GetBuildInfo();
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
            Router.Navigate.Execute(GetOrCreateViewModelForView(viewType)));

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
                    Log.Information("🔄 MANUAL RETRY: Starting immediate RestoreSecrecyChannel attempt");

                    Result<Utilities.Unit, NetworkFailure> recoveryResult =
                        await _networkProvider.ForceFreshConnectionAsync();

                    if (recoveryResult.IsOk)
                    {
                        Log.Information("🔄 MANUAL RETRY: RestoreSecrecyChannel succeeded - connection restored");
                        _networkEvents.InitiateChangeState(
                            NetworkStatusChangedEvent.New(NetworkStatus.DataCenterConnected));
                    }
                    else
                    {
                        Log.Warning("🔄 MANUAL RETRY: RestoreSecrecyChannel failed: {Error}", recoveryResult.UnwrapErr().Message);
                    }

                    return Unit.Default;
                })
                .Subscribe()
                .DisposeWith(disposables);
            Observable.Timer(TimeSpan.FromSeconds(2), RxApp.MainThreadScheduler)
                .SelectMany(_ => CheckCountryCultureMismatchCommand.Execute())
                .Subscribe(_ => { })
                .DisposeWith(disposables);
            Navigate.Execute(MembershipViewType.Welcome)
                .Subscribe()
                .DisposeWith(disposables);
        });
    }

    private async Task CheckCountryCultureMismatchAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> appSettings =
            await _applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync();

        if (appSettings.IsOk)
        {
            ApplicationInstanceSettings applicationInstanceSettings = appSettings.Unwrap();
            if (!string.IsNullOrEmpty(applicationInstanceSettings.Country) && applicationInstanceSettings.IsNewInstance)
            {
                string currentCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;

                string expectedCulture = LanguageConfig.GetCultureByCountry(applicationInstanceSettings.Country);

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

    private IRoutableViewModel GetOrCreateViewModelForView(MembershipViewType viewType, bool resetState = true)
    {
        if (_viewModelCache.TryGetValue(viewType, out WeakReference<IRoutableViewModel>? weakRef) &&
            weakRef.TryGetTarget(out IRoutableViewModel? cachedViewModel))
        {
            if (resetState && cachedViewModel is IResettable resettable)
            {
                resettable.ResetState();
            }
            return cachedViewModel;
        }

        if (!ViewModelFactories.TryGetValue(viewType,
                out Func<ISystemEvents, INetworkEvents, NetworkProvider, ILocalizationService, IAuthenticationService,
                    IApplicationSecureStorageProvider, MembershipHostWindowModel, IRoutableViewModel>? factory))
        {
            throw new ArgumentOutOfRangeException(nameof(viewType));
        }

        IRoutableViewModel newViewModel = factory(_systemEvents, _networkEvents, _networkProvider, LocalizationService,
            _authenticationService, _applicationSecureStorageProvider, this);
        _viewModelCache[viewType] = new WeakReference<IRoutableViewModel>(newViewModel);

        return newViewModel;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _connectivitySubscription?.Dispose();
            _disposables?.Dispose();
        }

        base.Dispose(disposing);
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}