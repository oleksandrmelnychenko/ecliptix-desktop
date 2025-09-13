using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Controls.Core;
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
using Ecliptix.Core.Features.Main.ViewModels;
using Ecliptix.Core.Views.Core;
using Ecliptix.Protobuf.Device;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Serilog;
using Splat;
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
    private readonly IOpaqueRegistrationService _opaqueRegistrationService;
    private readonly Dictionary<MembershipViewType, WeakReference<IRoutableViewModel>> _viewModelCache = new();
    private readonly CompositeDisposable _disposables = new();

    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    private static readonly FrozenDictionary<MembershipViewType, Func<ISystemEventService, INetworkEventService, NetworkProvider,
        ILocalizationService, IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel,
        IOpaqueRegistrationService, IRoutableViewModel>> ViewModelFactories =
        new Dictionary<MembershipViewType, Func<ISystemEventService, INetworkEventService, NetworkProvider, ILocalizationService,
            IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel, IOpaqueRegistrationService, IRoutableViewModel>>
        {
            [MembershipViewType.SignIn] = (sys, netEvents, netProvider, loc, auth, storage, host, reg) =>
                new SignInViewModel(sys, netEvents, netProvider, loc, auth, host),
            [MembershipViewType.Welcome] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg) => new WelcomeViewModel(host, sys, loc, netProvider),
            [MembershipViewType.MobileVerification] = (sys, netEvents, netProvider, loc, auth, storage, host, reg) =>
                new MobileVerificationViewModel(sys, netProvider, loc, host, storage, reg),
            [MembershipViewType.ConfirmSecureKey] = (sys, netEvents, netProvider, loc, auth, storage, host, reg) =>
                new SecureKeyVerifierViewModel(sys, netProvider, loc, host, storage, reg),
            [MembershipViewType.PassPhase] = (sys, netEvents, netProvider, loc, auth, storage, host, reg) =>
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

    public ReactiveCommand<Unit, Unit> SwitchToMainWindowCommand { get; }

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
        NetworkStatusNotificationViewModel networkStatusNotification,
        IOpaqueRegistrationService opaqueRegistrationService)
        : base(systemEventService, networkProvider, localizationService)
    {
        _networkEventService = networkEventService;
        _bottomSheetService = bottomSheetService;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _systemEventService = systemEventService;
        _networkProvider = networkProvider;
        _authenticationService = authenticationService;
        _opaqueRegistrationService = opaqueRegistrationService;

        LanguageSelector =
            new LanguageSelectorViewModel(localizationService, applicationSecureStorageProvider, rpcMetaDataProvider);
        NetworkStatusNotification = networkStatusNotification;

        _disposables.Add(NetworkStatusNotification);

        AppVersion = VersionHelper.GetApplicationVersion();
        BuildInfo? buildInfo = VersionHelper.GetBuildInfo();
        BuildInfo = buildInfo?.BuildNumber ?? "development";
        FullVersionInfo = $"{VersionHelper.GetDisplayVersion()}" +
                         (buildInfo != null ? $"\nBuild: {buildInfo.BuildNumber}\nCommit: {buildInfo.GitCommit[..8]}\nBranch: {buildInfo.GitBranch}" : "");

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
                if (_currentView is IResettable resettable)
                {
                    resettable.ResetState();
                }

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

        SwitchToMainWindowCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            try
            {
                Log.Information("Starting transition to main window after successful authentication");

                IModuleManager? moduleManager = Locator.Current.GetService<IModuleManager>();
                IWindowService? windowService = Locator.Current.GetService<IWindowService>();

                if (moduleManager == null || windowService == null)
                {
                    Log.Error("Required services not available for main window transition");
                    return;
                }

                Window? currentWindow = Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.Windows.FirstOrDefault(w => w.DataContext == this)
                    : null;

                if (currentWindow == null)
                {
                    Log.Error("Could not find current authentication window");
                    return;
                }

                Log.Information("Loading Main module...");
                IModule mainModule = await moduleManager.LoadModuleAsync("Main");
                Log.Information("Main module loaded successfully");

                Log.Information("Cleaning up authentication flow...");
                CleanupAuthenticationFlow();

                MainHostWindow mainWindow = new MainHostWindow();

                if (mainModule.ServiceScope?.ServiceProvider != null)
                {
                    try
                    {
                        MainViewModel? mainViewModel = mainModule.ServiceScope.ServiceProvider.GetService<MainViewModel>();
                        if (mainViewModel != null)
                        {
                            mainWindow.DataContext = mainViewModel;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Could not resolve MainViewModel from module scope, using default");
                    }
                }

                await windowService.ShowAndWaitForWindowAsync(mainWindow);
                windowService.PositionWindowRelativeTo(mainWindow, currentWindow);

                await windowService.PerformCrossfadeTransitionAsync(currentWindow, mainWindow);

                currentWindow.Close();

                Log.Information("Successfully transitioned to main window");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to transition to main window");
                throw;
            }
        });

        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/privacy"));
        OpenTermsOfServiceCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/terms"));
        OpenSupportCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/support"));

        this.WhenActivated(disposables =>
        {
            _networkEventService.OnManualRetryRequested(async e =>
                {
                    Result<Utilities.Unit, NetworkFailure> recoveryResult =
                        await _networkProvider.ForceFreshConnectionAsync();

                    if (recoveryResult.IsOk)
                    {
                        await _networkEventService.NotifyNetworkStatusAsync(NetworkStatus.DataCenterConnected);
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
    
    public async Task ShowRedirectNotificationAsync(UserControl redirectView, bool isDismissable)
    {
        try
        {
            await _bottomSheetService.ShowAsync(BottomSheetComponentType.RedirectNotification, redirectView, showScrim: true, isDismissable: false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to show redirect notification bottom sheet");
            throw;
        }
    }
    
    public async Task HideBottomSheetAsync()
    {
        try
        {
            await _bottomSheetService.HideAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to hide bottom sheet");
            throw;
        }
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
        try
        {
            Log.Information("Opening URL: {Url}", url);

            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.OSX))
            {
                System.Diagnostics.Process.Start("open", url);
            }
            else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux))
            {
                System.Diagnostics.Process.Start("xdg-open", url);
            }
            else
            {
                Log.Warning("Unsupported platform for opening URL: {Url}", url);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open URL: {Url}", url);
        }
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
                    IApplicationSecureStorageProvider, MembershipHostWindowModel, IOpaqueRegistrationService, IRoutableViewModel>? factory))
        {
            throw new ArgumentOutOfRangeException(nameof(viewType));
        }

        IRoutableViewModel newViewModel = factory(_systemEventService, _networkEventService, _networkProvider, LocalizationService,
            _authenticationService, _applicationSecureStorageProvider, this, _opaqueRegistrationService);
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
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Log.Information("MembershipHostWindowModel disposing - starting cleanup");

            CleanupAuthenticationFlow();

            _connectivitySubscription.Dispose();
            _disposables.Dispose();
            LanguageSelector.Dispose();
            NetworkStatusNotification.Dispose();

            Log.Information("MembershipHostWindowModel disposal complete");
        }

        base.Dispose(disposing);
    }

    public new void Dispose()
    {
        Dispose(true);
    }
}