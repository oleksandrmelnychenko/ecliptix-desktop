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
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Abstractions.Core;
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
using Ecliptix.Core.Features.Authentication.ViewModels.PasswordRecovery;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Features.Main.ViewModels;
using Ecliptix.Core.Views.Core;
using Ecliptix.Core.ViewModels.Core;
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
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IDisposable _connectivitySubscription;
    private IDisposable? _languageSubscription;
    private IDisposable? _bottomSheetHiddenSubscription;
    private readonly INetworkEventService _networkEventService;
    private readonly NetworkProvider _networkProvider;
    private readonly ILanguageDetectionService _languageDetectionService;
    private readonly ILocalizationService _localizationService;
    private readonly MainWindowViewModel _mainWindowViewModel;

    private readonly ISystemEventService _systemEventService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IOpaqueRegistrationService _opaqueRegistrationService;
    private readonly IPasswordRecoveryService _passwordRecoveryService;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IApplicationRouter _router;
    private readonly Dictionary<MembershipViewType, WeakReference<IRoutableViewModel>> _viewModelCache = new();
    private readonly CompositeDisposable _disposables = new();

    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    private static readonly FrozenDictionary<MembershipViewType, Func<ISystemEventService, INetworkEventService,
        NetworkProvider,
        ILocalizationService, IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel,
        IOpaqueRegistrationService, IPasswordRecoveryService, IUiDispatcher, IRoutableViewModel>> ViewModelFactories =
        new Dictionary<MembershipViewType, Func<ISystemEventService, INetworkEventService, NetworkProvider,
            ILocalizationService,
            IAuthenticationService, IApplicationSecureStorageProvider, MembershipHostWindowModel,
            IOpaqueRegistrationService, IPasswordRecoveryService, IUiDispatcher, IRoutableViewModel>>
        {
            [MembershipViewType.SignIn] = (sys, netEvents, netProvider, loc, auth, storage, host, reg, pwdRecovery, uiDispatcher) =>
                new SignInViewModel(sys, netEvents, netProvider, loc, auth, host),
            [MembershipViewType.Welcome] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, pwdRecovery, uiDispatcher) =>
                    new WelcomeViewModel(host, sys, loc, netProvider),
            [MembershipViewType.MobileVerification] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, pwdRecovery, uiDispatcher) =>
                    new MobileVerificationViewModel(sys, netProvider, loc, host, storage, reg, uiDispatcher),
            [MembershipViewType.ConfirmSecureKey] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, pwdRecovery, uiDispatcher) =>
                    new SecureKeyVerifierViewModel(sys, netProvider, loc, host, storage, reg, auth),
            [MembershipViewType.PassPhase] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, pwdRecovery, uiDispatcher) =>
                    new PassPhaseViewModel(sys, loc, host, netProvider),
            [MembershipViewType.ForgotPasswordReset] =
                (sys, netEvents, netProvider, loc, auth, storage, host, reg, pwdRecovery, uiDispatcher) =>
                    new ForgotPasswordResetViewModel(sys, netProvider, loc, host, storage, pwdRecovery, auth)
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

    public NetworkStatusNotificationViewModel NetworkStatusNotification { get; }

    public string AppVersion { get; }

    public string BuildInfo { get; }

    public string FullVersionInfo { get; }

    public string? RegistrationMobileNumber { get; set; }
    public string? RecoveryMobileNumber { get; set; }

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    public ReactiveCommand<Unit, IRoutableViewModel?> NavigateBack { get; }

    public ReactiveCommand<Unit, Unit> SwitchToMainWindowCommand { get; }

    public void ClearNavigationStack(bool preserveInitialWelcome = false)
    {
        _navigationStack.Clear();

        if (preserveInitialWelcome)
        {
            try
            {
                IRoutableViewModel welcomeView = GetOrCreateViewModelForView(MembershipViewType.Welcome, resetState: true);
                _navigationStack.Push(welcomeView);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to preserve welcome view in navigation stack");
            }
        }
        CanNavigateBack = _navigationStack.Count > 0;
        Log.Information("Navigation stack cleared{Preserve}", preserveInitialWelcome ? " (preserved welcome)" : "");
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

    public void StartPasswordRecoveryFlow()
    {
        ClearNavigationStack(true);
        MobileVerificationViewModel vm = new(
            _systemEventService,
            _networkProvider,
            LocalizationService,
            this,
            _applicationSecureStorageProvider,
            _opaqueRegistrationService,
            _uiDispatcher,
            AuthenticationFlowContext.PasswordRecovery,
            _passwordRecoveryService);
        NavigateToViewModel(vm);
    }

    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenSupportCommand { get; }

    public ReactiveCommand<Unit, Unit> CheckCountryCultureMismatchCommand { get; }

    public MembershipHostWindowModel(
        ISystemEventService systemEventService,
        INetworkEventService networkEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IInternetConnectivityObserver connectivityObserver,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IAuthenticationService authenticationService,
        IOpaqueRegistrationService opaqueRegistrationService,
        IPasswordRecoveryService passwordRecoveryService,
        IUiDispatcher uiDispatcher,
        ILanguageDetectionService languageDetectionService,
        IApplicationRouter router,
        MainWindowViewModel mainWindowViewModel)
        : base(systemEventService, networkProvider, localizationService)
    {
        Log.Information("[MEMBERSHIP-HOST-CTOR] Constructor started");
        _localizationService = localizationService;
        _networkEventService = networkEventService;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _systemEventService = systemEventService;
        _networkProvider = networkProvider;
        _authenticationService = authenticationService;
        _opaqueRegistrationService = opaqueRegistrationService;
        _passwordRecoveryService = passwordRecoveryService;
        _uiDispatcher = uiDispatcher;
        _languageDetectionService = languageDetectionService;
        _router = router;
        _mainWindowViewModel = mainWindowViewModel;

        NetworkStatusNotification = mainWindowViewModel.NetworkStatusNotification;

        AppVersion = VersionHelper.GetApplicationVersion();
        BuildInfo? buildInfo = VersionHelper.GetBuildInfo();
        BuildInfo = buildInfo?.BuildNumber ?? "development";

        if (buildInfo != null)
        {
            FullVersionInfo = string.Concat(
                VersionHelper.GetDisplayVersion(),
                "\nBuild: ", buildInfo.BuildNumber,
                "\nCommit: ", buildInfo.GitCommit.Substring(0, 8),
                "\nBranch: ", buildInfo.GitBranch);
        }
        else
        {
            FullVersionInfo = VersionHelper.GetDisplayVersion();
        }

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
            if (moduleManager == null)
            {
                Log.Error("[MEMBERSHIP-HOST] Failed to get IModuleManager");
                return;
            }

            IModule mainModule = await moduleManager.LoadModuleAsync("Main");

            CleanupAuthenticationFlow();
            await _router.NavigateToMainAsync();
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

        Log.Information("[MEMBERSHIP-HOST-CTOR] Constructor completed");
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

            await _mainWindowViewModel.HideBottomSheetAsync().ConfigureAwait(false);
        }
        finally
        {
            _languageSubscription?.Dispose();
            _bottomSheetHiddenSubscription?.Dispose();
        }
    }

    private void ChangeApplicationLanguage(string targetCulture)
    {
        _localizationService.SetCulture(targetCulture,
            () =>
            {
                _applicationSecureStorageProvider.SetApplicationSettingsCultureAsync(targetCulture).ConfigureAwait(false);
            });
    }

    private Task HandleBottomSheetDismissedEvent(BottomSheetHiddenEvent evt)
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
            _languageSubscription?.Dispose();
            _bottomSheetHiddenSubscription?.Dispose();
        }

        return Task.CompletedTask;
    }

    public async Task ShowBottomSheet(BottomSheetComponentType componentType, UserControl redirectView,
        bool showScrim = true, bool isDismissable = false)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await _uiDispatcher.PostAsync(async () =>
            {
                await _mainWindowViewModel.ShowBottomSheetAsync(componentType, redirectView,
                    showScrim: showScrim, isDismissable: isDismissable).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        else
        {
            await _mainWindowViewModel.ShowBottomSheetAsync(componentType, redirectView,
                showScrim: showScrim, isDismissable: isDismissable).ConfigureAwait(false);
        }
    }

    public async Task HideBottomSheetAsync()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await _uiDispatcher.PostAsync(async () =>
            {
                await _mainWindowViewModel.HideBottomSheetAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        else
        {
            await _mainWindowViewModel.HideBottomSheetAsync().ConfigureAwait(false);
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
                    _mainWindowViewModel.OnBottomSheetHidden(HandleBottomSheetDismissedEvent,
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

                    await _mainWindowViewModel.ShowBottomSheetAsync(
                        BottomSheetComponentType.DetectedLocalization,
                        detectLanguageView,
                        showScrim: true,
                        isDismissable: true
                    ).ConfigureAwait(false);
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
                    IPasswordRecoveryService, IUiDispatcher, IRoutableViewModel>? factory))
        {
            throw new InvalidOperationException($"No factory registered for view type: {viewType}");
        }

        IRoutableViewModel newViewModel = factory(_systemEventService, _networkEventService, _networkProvider,
            LocalizationService,
            _authenticationService, _applicationSecureStorageProvider, this, _opaqueRegistrationService,
            _passwordRecoveryService, _uiDispatcher);
        _viewModelCache[viewType] = new WeakReference<IRoutableViewModel>(newViewModel);

        return newViewModel;
    }

    public void CleanupAuthenticationFlow()
    {
        ClearNavigationStack();

        List<KeyValuePair<MembershipViewType, WeakReference<IRoutableViewModel>>> cachedItems =
            _viewModelCache.ToList();

        foreach (KeyValuePair<MembershipViewType, WeakReference<IRoutableViewModel>> item in cachedItems)
        {
            if (!item.Value.TryGetTarget(out IRoutableViewModel? viewModel)) continue;
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
        }

        base.Dispose(disposing);
    }

    public new void Dispose()
    {
        Dispose(true);
    }
}
