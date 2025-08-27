using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Controls.LanguageSelector;
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
using Ecliptix.Protobuf.Device;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using ReactiveUI;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Hosts;

public class MembershipHostWindowModel : Core.MVVM.ViewModelBase, IScreen, IDisposable
{
    private bool _canNavigateBack;
    private readonly IBottomSheetService _bottomSheetService;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IDisposable _connectivitySubscription;
    private readonly INetworkEventService _networkEventService;
    private readonly NetworkProvider _networkProvider;

    private readonly ISystemEventService _systemEventService;
    private readonly IAuthenticationService _authenticationService;
    private readonly Dictionary<MembershipViewType, WeakReference<IRoutableViewModel>> _viewModelCache = new();
    private readonly CompositeDisposable _disposables = new();

    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    private static readonly FrozenDictionary<MembershipViewType, Func<ISystemEventService, INetworkEventService, NetworkProvider,
        ILocalizationService, IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel,
        IRoutableViewModel>> ViewModelFactories =
        new Dictionary<MembershipViewType, Func<ISystemEventService, INetworkEventService, NetworkProvider, ILocalizationService,
            IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel, IRoutableViewModel>>
        {
            [MembershipViewType.SignIn] = (sys, netEvents, netProvider, loc, auth, storage, host) =>
                new SignInViewModel(sys, netEvents, netProvider, loc, auth, host),
            [MembershipViewType.Welcome] =
                (sys, netEvents, netProvider, loc, auth, storage, host) => new WelcomeViewModel(host, sys, loc, netProvider),
            [MembershipViewType.MobileVerification] = (sys, netEvents, netProvider, loc, auth, storage, host) =>
                new MobileVerificationViewModel(sys, netProvider, loc, host, storage),
            [MembershipViewType.ConfirmSecureKey] = (sys, netEvents, netProvider, loc, auth, storage, host) =>
                new SecureKeyVerifierViewModel(sys, netProvider, loc, host, storage),
            [MembershipViewType.PassPhase] = (sys, netEvents, netProvider, loc, auth, storage, host) =>
                new PassPhaseViewModel(sys, loc, host, netProvider)
        }.ToFrozenDictionary();

    private readonly Stack<IRoutableViewModel> _navigationStack = new();
    
    public RoutingState Router { get; } = new();
    
    private IRoutableViewModel? _currentView;
    public IRoutableViewModel? CurrentView
    {
        get => _currentView;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentView, value);
            
            CanNavigateBack = _navigationStack.Count > 0;
        }
    }

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
    
    public ReactiveCommand<Unit, IRoutableViewModel?> NavigateBack { get; }
    
    public void ClearNavigationStack()
    {
        _navigationStack.Clear();
        CanNavigateBack = false;
        Log.Information("Navigation stack cleared");
    }
    
    public void NavigateToViewModel(IRoutableViewModel viewModel)
    {
        if (_currentView != null)
        {
            _navigationStack.Push(_currentView);
            Log.Information("Pushed {ViewModelType} to navigation stack. Stack size: {Size}", 
                _currentView.GetType().Name, _navigationStack.Count);
        }

        CurrentView = viewModel;
        Log.Information("Navigated directly to {ViewModelType}", viewModel.GetType().Name);
    }

    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenSupportCommand { get; }

    public ReactiveCommand<Unit, Unit> CheckCountryCultureMismatchCommand { get; }

    public MembershipHostWindowModel(
        IBottomSheetService bottomSheetService,
        ISystemEventService systemEventService,
        INetworkEventService networkEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        InternetConnectivityObserver connectivityObserver,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IRpcMetaDataProvider rpcMetaDataProvider,
        IAuthenticationService authenticationService,
        NetworkStatusNotificationViewModel networkStatusNotification)
        : base(systemEventService, networkProvider, localizationService)
    {
        _networkEventService = networkEventService;
        _bottomSheetService = bottomSheetService;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _systemEventService = systemEventService;
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

            _ = _networkEventService.NotifyNetworkStatusAsync(status
                ? NetworkStatus.DataCenterConnected
                : NetworkStatus.NoInternet);
        });

        Navigate = ReactiveCommand.Create<MembershipViewType, IRoutableViewModel>(viewType =>
        {
            Log.Information("Navigate command executing for ViewType: {ViewType}", viewType);
            IRoutableViewModel viewModel = GetOrCreateViewModelForView(viewType);
            Log.Information("Created/Retrieved ViewModel: {ViewModelType} for ViewType: {ViewType}",
                viewModel.GetType().Name, viewType);

            if (_currentView != null)
            {
                _navigationStack.Push(_currentView);
                Log.Information("Pushed {ViewModelType} to navigation stack. Stack size: {Size}", 
                    _currentView.GetType().Name, _navigationStack.Count);
            }

            CurrentView = viewModel;
            
            return viewModel;
        });
        
        NavigateBack = ReactiveCommand.Create(() =>
        {
            if (_navigationStack.Count > 0)
            {
                IRoutableViewModel previousView = _navigationStack.Pop();
                Log.Information("Navigating back to {ViewModelType}. Stack size: {Size}", 
                    previousView.GetType().Name, _navigationStack.Count);
                
                _currentView = previousView;
                this.RaisePropertyChanged(nameof(CurrentView));
                CanNavigateBack = _navigationStack.Count > 0;
                
                return previousView;
            }
            
            Log.Information("Cannot navigate back - navigation stack is empty");
            return null;
        });

        CheckCountryCultureMismatchCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await CheckCountryCultureMismatchAsync();
            return Unit.Default;
        });

        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/privacy"));
        OpenTermsOfServiceCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/terms"));
        OpenSupportCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/support"));

        this.WhenActivated(disposables =>
        {
            _networkEventService.OnManualRetryRequested(async e =>
                {
                    Log.Information("🔄 MANUAL RETRY: Starting immediate RestoreSecrecyChannel attempt");

                    Result<Utilities.Unit, NetworkFailure> recoveryResult =
                        await _networkProvider.ForceFreshConnectionAsync();

                    if (recoveryResult.IsOk)
                    {
                        Log.Information("🔄 MANUAL RETRY: RestoreSecrecyChannel succeeded - connection restored");
                        await _networkEventService.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
                    }
                    else
                    {
                        Log.Warning("🔄 MANUAL RETRY: RestoreSecrecyChannel failed: {Error}", recoveryResult.UnwrapErr().Message);
                    }
                })
                .DisposeWith(disposables);
            Observable.Timer(TimeSpan.FromSeconds(2), RxApp.MainThreadScheduler)
                .SelectMany(_ => CheckCountryCultureMismatchCommand.Execute())
                .Subscribe(_ => { })
                .DisposeWith(disposables);
            Navigate.Execute(MembershipViewType.Welcome)
                .Subscribe(vm =>
                {
                    Log.Information("Navigation to Welcome completed. ViewModel: {ViewModelType}, UrlPath: {UrlPath}",
                        vm.GetType().Name, vm.UrlPathSegment);
                })
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
                    await _bottomSheetService.ShowAsync(BottomSheetComponentType.DetectedLocalization);
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
                out Func<ISystemEventService, INetworkEventService, NetworkProvider, ILocalizationService, IAuthenticationService,
                    IApplicationSecureStorageProvider, MembershipHostWindowModel, IRoutableViewModel>? factory))
        {
            throw new ArgumentOutOfRangeException(nameof(viewType));
        }

        IRoutableViewModel newViewModel = factory(_systemEventService, _networkEventService, _networkProvider, LocalizationService,
            _authenticationService, _applicationSecureStorageProvider, this);
        _viewModelCache[viewType] = new WeakReference<IRoutableViewModel>(newViewModel);

        return newViewModel;
    }

    public void CleanupAuthenticationFlow()
    {
        Log.Information("Starting authentication flow cleanup");

        ClearNavigationStack();

        foreach ((MembershipViewType viewType, WeakReference<IRoutableViewModel> weakRef) in _viewModelCache)
        {
            if (weakRef.TryGetTarget(out IRoutableViewModel? viewModel))
            {
                Log.Information("Disposing cached ViewModel: {ViewModelType}", viewModel.GetType().Name);
                
                if (viewModel is IDisposable disposableViewModel)
                {
                    disposableViewModel.Dispose();
                }
                
                if (viewModel is IResettable resettableViewModel)
                {
                    resettableViewModel.ResetState();
                }
            }
        }

        _viewModelCache.Clear();
        
        CurrentView = null;

        Log.Information("Authentication flow cleanup completed - all ViewModels disposed and cache cleared");
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Log.Information("MembershipHostWindowModel disposing - starting cleanup");
            
            CleanupAuthenticationFlow();
            
            _connectivitySubscription?.Dispose();
            _disposables?.Dispose();
            LanguageSelector?.Dispose();
            NetworkStatusNotification?.Dispose();
            
            Log.Information("MembershipHostWindowModel disposal complete");
        }

        base.Dispose(disposing);
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}