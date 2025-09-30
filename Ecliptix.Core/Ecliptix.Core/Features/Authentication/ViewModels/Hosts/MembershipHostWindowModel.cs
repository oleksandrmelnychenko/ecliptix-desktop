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
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.LanguageSelector;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Transport;
using Ecliptix.Core.Infrastructure.Network.Core.Connectivity;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Welcome;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.SignIn;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging;
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
    private IDisposable _languageSubscription;
    private IDisposable _bottomSheetHiddenSubscription;
    private readonly INetworkEventService _networkEventService;
    private readonly NetworkProvider _networkProvider;
    private readonly ILanguageDetectionService _languageDetectionService;
    private readonly ILocalizationService _localizationService;
    private readonly IRpcMetaDataProvider _rpcMetaDataProvider;

    private readonly ISystemEventService _systemEventService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IOpaqueRegistrationService _opaqueRegistrationService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Dictionary<MembershipViewType, WeakReference<IRoutableViewModel>> _viewModelCache = new();
    private readonly CompositeDisposable _disposables = new();

    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    private static readonly FrozenDictionary<MembershipViewType, Func<ISystemEventService, INetworkEventService,
        NetworkProvider,
        ILocalizationService, IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel,
        IOpaqueRegistrationService, IUiDispatcher, IRoutableViewModel>> ViewModelFactories =
        new Dictionary<MembershipViewType, Func<ISystemEventService, INetworkEventService, NetworkProvider,
            ILocalizationService,
            IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel,
            IOpaqueRegistrationService, IUiDispatcher, IRoutableViewModel>>
        {
            [MembershipViewType.SignIn] = (sys, netEvents, netProvider, loc, auth, storage, host, reg, uiDispatcher) =>
                new SignInViewModel(sys, netEvents, netProvider, loc, auth, host),
            [MembershipViewType.Welcome] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, uiDispatcher) =>
                    new WelcomeViewModel(host, sys, loc, netProvider),
            [MembershipViewType.MobileVerification] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, uiDispatcher) =>
                    new MobileVerificationViewModel(sys, netProvider, loc, host, storage, reg, uiDispatcher),
            [MembershipViewType.ConfirmSecureKey] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, uiDispatcher) =>
                    new SecureKeyVerifierViewModel(sys, netProvider, loc, host, storage, reg, auth),
            [MembershipViewType.PassPhase] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, uiDispatcher) =>
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

    public string? RegistrationMobileNumber { get; set; }

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
            if (_currentView is IResettable currentResettable)
            {
                currentResettable.ResetState();
            }

            _navigationStack.Push(_currentView);
        }

        CurrentView = viewModel;
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
        IOpaqueRegistrationService opaqueRegistrationService,
        IUiDispatcher uiDispatcher,
        ILanguageDetectionService languageDetectionService)
        : base(systemEventService, networkProvider, localizationService)
    {
        _rpcMetaDataProvider = rpcMetaDataProvider;
        _localizationService = localizationService;
        _networkEventService = networkEventService;
        _bottomSheetService = bottomSheetService;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _systemEventService = systemEventService;
        _networkProvider = networkProvider;
        _authenticationService = authenticationService;
        _opaqueRegistrationService = opaqueRegistrationService;
        _uiDispatcher = uiDispatcher;
        _languageDetectionService = languageDetectionService;

        LanguageSelector =
            new LanguageSelectorViewModel(localizationService, applicationSecureStorageProvider, rpcMetaDataProvider);
        NetworkStatusNotification = networkStatusNotification;

        _disposables.Add(NetworkStatusNotification);

        AppVersion = VersionHelper.GetApplicationVersion();
        BuildInfo? buildInfo = VersionHelper.GetBuildInfo();
        BuildInfo = buildInfo?.BuildNumber ?? "development";
        FullVersionInfo = $"{VersionHelper.GetDisplayVersion()}" +
                          (buildInfo != null
                              ? $"\nBuild: {buildInfo.BuildNumber}\nCommit: {buildInfo.GitCommit[..8]}\nBranch: {buildInfo.GitBranch}"
                              : "");

        _connectivitySubscription = connectivityObserver.Subscribe(status =>
        {
            _ = _networkEventService.NotifyNetworkStatusAsync(status
                ? NetworkStatus.DataCenterConnected
                : NetworkStatus.NoInternet);
        });

        Navigate = ReactiveCommand.Create<MembershipViewType, IRoutableViewModel>(viewType =>
        {
            IRoutableViewModel viewModel = GetOrCreateViewModelForView(viewType);

            if (_currentView != null)
            {
                _navigationStack.Push(_currentView);
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

                _currentView = previousView;
                this.RaisePropertyChanged(nameof(CurrentView));
                CanNavigateBack = _navigationStack.Count > 0;

                return previousView;
            }

            return null;
        });

        CheckCountryCultureMismatchCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await CheckCountryCultureMismatchAsync();
            return Unit.Default;
        });

        SwitchToMainWindowCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            IModuleManager? moduleManager = Locator.Current.GetService<IModuleManager>();
            IWindowService? windowService = Locator.Current.GetService<IWindowService>();

            if (moduleManager == null || windowService == null)
            {
                return;
            }

            Window? currentWindow =
                Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.Windows.FirstOrDefault(w => w.DataContext == this)
                    : null;

            if (currentWindow == null)
            {
                return;
            }

            IModule mainModule = await moduleManager.LoadModuleAsync("Main");

            CleanupAuthenticationFlow();

            MainHostWindow mainWindow = new();

            if (mainModule.ServiceScope?.ServiceProvider != null)
            {
                MainViewModel? mainViewModel =
                    mainModule.ServiceScope.ServiceProvider.GetService<MainViewModel>();
                if (mainViewModel != null)
                {
                    mainWindow.DataContext = mainViewModel;
                }
            }

            await windowService.ShowAndWaitForWindowAsync(mainWindow);
            windowService.PositionWindowRelativeTo(mainWindow, currentWindow);

            await windowService.PerformCrossfadeTransitionAsync(currentWindow, mainWindow);

            currentWindow.Close();
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
                .Subscribe(_ => { })
                .DisposeWith(disposables);
        });
    }

    private async Task HandleLanguageDetectionEvent(LanguageDetectionDialogEvent evt)
    {
        try
        {
            switch (evt.Action)
            {
                case LanguageDetectionAction.Confirm when !string.IsNullOrEmpty(evt.TargetCulture):
                    ChangeApplicationLanguage(evt.TargetCulture);
                    break;

                case LanguageDetectionAction.Decline:
                    break;
            }

            await _bottomSheetService.HideAsync();
        }
        finally
        {
            _languageSubscription.Dispose();
            _bottomSheetHiddenSubscription.Dispose();
        }
    }

    private async Task HandleBottomSheetDismissedEvent(BottomSheetHiddenEvent evt)
    {
        try
        {
            if (evt.WasDismissedByUser)
            {
                Log.Information("Bottom sheet dismissed by user");
            }
        }
        finally
        {
            _languageSubscription.Dispose();
            _bottomSheetHiddenSubscription.Dispose();
        }
    }

    private void ChangeApplicationLanguage(string targetCulture)
    {
        _localizationService.SetCulture(targetCulture,
            () =>
            {
                _applicationSecureStorageProvider.SetApplicationSettingsCultureAsync(targetCulture);
                _rpcMetaDataProvider.SetCulture(targetCulture);
            });
    }

    public async Task ShowBottomSheet(BottomSheetComponentType componentType, UserControl redirectView,
        bool showScrim = true, bool isDismissable = false)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await _uiDispatcher.PostAsync(async () =>
            {
                await _bottomSheetService.ShowAsync(componentType, redirectView,
                    showScrim: showScrim, isDismissable: isDismissable);
            });
        }
        else
        {
            await _bottomSheetService.ShowAsync(componentType, redirectView,
                showScrim: showScrim, isDismissable: isDismissable);
        }
    }

    public async Task HideBottomSheetAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await _uiDispatcher.PostAsync(async () => { await _bottomSheetService.HideAsync(); });
        }
        else
        {
            await _bottomSheetService.HideAsync();
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
                _languageSubscription =
                    _languageDetectionService.OnLanguageDetectionRequested(HandleLanguageDetectionEvent,
                        SubscriptionLifetime.Scoped);
                _bottomSheetHiddenSubscription =
                    _bottomSheetService.OnBottomSheetHidden(HandleBottomSheetDismissedEvent,
                        SubscriptionLifetime.Scoped);

                string currentCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;

                string expectedCulture = LanguageConfig.GetCultureByCountry(applicationInstanceSettings.Country);

                if (!string.Equals(currentCulture, expectedCulture, StringComparison.OrdinalIgnoreCase))
                {
                    DetectLanguageDialogViewModel detectLanguageViewModel = new(
                        LocalizationService,
                        _languageDetectionService,
                        _networkProvider
                    );

                    DetectLanguageDialog detectLanguageView = new()
                    {
                        DataContext = detectLanguageViewModel
                    };

                    await _bottomSheetService.ShowAsync(
                        BottomSheetComponentType.DetectedLocalization,
                        detectLanguageView,
                        showScrim: true,
                        isDismissable: true
                    );
                }
            }
        }
    }

    private static void OpenUrl(string url)
    {
        Log.Information("Opening URL: {Url}", url);

        if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform
                .Windows))
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices
                     .OSPlatform.OSX))
        {
            System.Diagnostics.Process.Start("open", url);
        }
        else if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices
                     .OSPlatform.Linux))
        {
            System.Diagnostics.Process.Start("xdg-open", url);
        }
        else
        {
            Log.Warning("Unsupported platform for opening URL: {Url}", url);
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
                out Func<ISystemEventService, INetworkEventService, NetworkProvider, ILocalizationService,
                    IAuthenticationService,
                    IApplicationSecureStorageProvider, MembershipHostWindowModel, IOpaqueRegistrationService,
                    IUiDispatcher, IRoutableViewModel>? factory))
        {
        }

        IRoutableViewModel newViewModel = factory(_systemEventService, _networkEventService, _networkProvider,
            LocalizationService,
            _authenticationService, _applicationSecureStorageProvider, this, _opaqueRegistrationService,
            _uiDispatcher);
        _viewModelCache[viewType] = new WeakReference<IRoutableViewModel>(newViewModel);

        return newViewModel;
    }

    public void CleanupAuthenticationFlow()
    {
        ClearNavigationStack();

        List<KeyValuePair<MembershipViewType, WeakReference<IRoutableViewModel>>> cachedItems =
            _viewModelCache.ToList();

        foreach (WeakReference<IRoutableViewModel> weakRef in cachedItems.Select(item => item.Value))
        {
            if (!weakRef.TryGetTarget(out IRoutableViewModel? viewModel)) continue;
            if (viewModel is IResettable resettableViewModel)
            {
                resettableViewModel.ResetState();
            }

            if (viewModel is IDisposable disposableViewModel)
            {
                disposableViewModel.Dispose();
            }
        }

        _viewModelCache.Clear();
        CurrentView = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanupAuthenticationFlow();

            _connectivitySubscription.Dispose();
            _disposables.Dispose();
            LanguageSelector.Dispose();
            NetworkStatusNotification.Dispose();
        }

        base.Dispose(disposing);
    }

    public new void Dispose()
    {
        Dispose(true);
    }
}