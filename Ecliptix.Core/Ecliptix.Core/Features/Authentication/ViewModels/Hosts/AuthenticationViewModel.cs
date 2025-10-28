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
using Avalonia.Threading;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;
using Ecliptix.Core.Features.Authentication.ViewModels.SignIn;
using Ecliptix.Core.Features.Authentication.ViewModels.Welcome;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels.Core;
using Ecliptix.Protobuf.Device;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using ReactiveUI;
using Serilog;
using Splat;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Hosts;

public class AuthenticationViewModel : Core.MVVM.ViewModelBase, IScreen, IDisposable
{
    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    private static readonly FrozenDictionary<MembershipViewType, Func<IConnectivityService,
        NetworkProvider,
        ILocalizationService, IAuthenticationService, IApplicationSecureStorageProvider, AuthenticationViewModel,
        IOpaqueRegistrationService, IPasswordRecoveryService, AuthenticationFlowContext, IRoutableViewModel>> ViewModelFactories =
        new Dictionary<MembershipViewType, Func<IConnectivityService, NetworkProvider,
            ILocalizationService,
            IAuthenticationService, IApplicationSecureStorageProvider, AuthenticationViewModel,
            IOpaqueRegistrationService, IPasswordRecoveryService, AuthenticationFlowContext, IRoutableViewModel>>
        {
            [MembershipViewType.SignIn] = (netEvents, netProvider, loc, auth, _, host, _, _,_) =>
                new SignInViewModel(netEvents, netProvider, loc, auth, host),
            [MembershipViewType.Welcome] =
                (_, netProvider, loc, _, _, host, _, _, _) =>
                    new WelcomeViewModel(host, loc, netProvider),
            [MembershipViewType.MobileVerification] =
                (netEvents, netProvider, loc, auth, storage, host, reg, pwdRecovery, flowContext) =>
                    new MobileVerificationViewModel(netEvents, netProvider, loc, host, storage, reg, auth, pwdRecovery, flowContext),
            [MembershipViewType.ConfirmSecureKey] =
                (netEvents, netProvider, loc, auth, storage, host, reg, pwdRecovery, flowContext) =>
                    new SecureKeyVerifierViewModel(netEvents, netProvider, loc, host, storage, reg, auth, pwdRecovery, flowContext),
            [MembershipViewType.PassPhase] =
                (netEvents, netProvider, loc, _, _, host, _, _, _) =>
                    new PassPhaseViewModel(loc, host, netProvider),
        }.ToFrozenDictionary();

    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IConnectivityService _connectivityService;
    private readonly NetworkProvider _networkProvider;
    private readonly ILanguageDetectionService _languageDetectionService;
    private readonly ILocalizationService _localizationService;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IAuthenticationService _authenticationService;
    private readonly IOpaqueRegistrationService _opaqueRegistrationService;
    private readonly IPasswordRecoveryService _passwordRecoveryService;
    private readonly IApplicationRouter _router;
    private readonly Dictionary<(MembershipViewType ViewType, AuthenticationFlowContext FlowContext), WeakReference<IRoutableViewModel>> _viewModelCache = new();
    private readonly Stack<IRoutableViewModel> _navigationStack = new();
    private readonly CompositeDisposable _disposables = new();

    private bool _canNavigateBack;
    private IDisposable? _languageSubscription;
    private IDisposable? _bottomSheetHiddenSubscription;
    private IRoutableViewModel? _currentView;

    public AuthenticationFlowContext CurrentFlowContext { get; set; } = AuthenticationFlowContext.Registration;

    public AuthenticationViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IAuthenticationService authenticationService,
        IOpaqueRegistrationService opaqueRegistrationService,
        IPasswordRecoveryService passwordRecoveryService,
        ILanguageDetectionService languageDetectionService,
        IApplicationRouter router,
        MainWindowViewModel mainWindowViewModel)
        : base(networkProvider, localizationService, null)
    {
        _localizationService = localizationService;
        _connectivityService = connectivityService;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _networkProvider = networkProvider;
        _authenticationService = authenticationService;
        _opaqueRegistrationService = opaqueRegistrationService;
        _passwordRecoveryService = passwordRecoveryService;
        _languageDetectionService = languageDetectionService;
        _router = router;
        _mainWindowViewModel = mainWindowViewModel;

        ConnectivityNotification = mainWindowViewModel.ConnectivityNotification;

        AppVersion = VersionHelper.GetApplicationVersion();
        Option<BuildInfo> buildInfo = VersionHelper.GetBuildInfo();
        BuildInfo = buildInfo.Select(bi => bi.BuildNumber).GetValueOrDefault("development");

        FullVersionInfo = buildInfo.Match(
            bi => string.Concat(
                VersionHelper.GetDisplayVersion(),
                "\nBuild: ", bi.BuildNumber,
                "\nCommit: ", bi.GitCommit.Substring(0, 8),
                "\nBranch: ", bi.GitBranch),
            () => VersionHelper.GetDisplayVersion());

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

            await moduleManager.LoadModuleAsync("Main");

            CleanupAuthenticationFlow();
            await _router.NavigateToMainAsync();
        });

        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/privacy"));
        OpenTermsOfServiceCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/terms"));
        OpenSupportCommand = ReactiveCommand.Create(() => OpenUrl("https://ecliptix.com/support"));

        this.WhenActivated(disposables =>
        {
            _connectivityService.OnManualRetryRequested(async e =>
                {
                    Result<Utilities.Unit, NetworkFailure> recoveryResult =
                        await _networkProvider.ForceFreshConnectionAsync();

                    if (recoveryResult.IsOk)
                    {
                        ConnectivityIntent intent =
                            ConnectivityIntent.Connected(e.ConnectId, ConnectivityReason.ManualRetry)
                                with
                                {
                                    Source = ConnectivitySource.ManualAction
                                };
                        await _connectivityService.PublishAsync(intent);
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

    public RoutingState Router => new();

    public ConnectivityNotificationViewModel ConnectivityNotification { get; }

    public string AppVersion { get; }

    public string BuildInfo { get; }

    public string FullVersionInfo { get; }

    public string? RegistrationMobileNumber { get; set; }

    public string? RecoveryMobileNumber { get; set; }

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; }

    public ReactiveCommand<Unit, IRoutableViewModel?> NavigateBack { get; }

    public ReactiveCommand<Unit, Unit> SwitchToMainWindowCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; }

    public ReactiveCommand<Unit, Unit> OpenSupportCommand { get; }

    public ReactiveCommand<Unit, Unit> CheckCountryCultureMismatchCommand { get; }

    public void ClearNavigationStack(bool preserveInitialWelcome = false)
    {
        _navigationStack.Clear();

        if (preserveInitialWelcome)
        {
            try
            {
                IRoutableViewModel welcomeView =
                    GetOrCreateViewModelForView(MembershipViewType.Welcome, resetState: true);
                _navigationStack.Push(welcomeView);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to preserve welcome view in navigation stack");
            }
        }

        CanNavigateBack = _navigationStack.Count > 0;
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
        CurrentFlowContext = AuthenticationFlowContext.PasswordRecovery;
        Navigate.Execute(MembershipViewType.MobileVerification).Subscribe();
    }

    public async Task ShowBottomSheet(BottomSheetComponentType componentType, UserControl redirectView,
        bool showScrim = true, bool isDismissable = false)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
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
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                await _mainWindowViewModel.HideBottomSheetAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        else
        {
            await _mainWindowViewModel.HideBottomSheetAsync().ConfigureAwait(false);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanupAuthenticationFlow();

            _disposables.Dispose();
        }

        base.Dispose(disposing);
    }

    private static void OpenUrl(string url)
    {
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

    private void CleanupAuthenticationFlow()
    {
        ClearNavigationStack();

        List<KeyValuePair< (MembershipViewType viewType, AuthenticationFlowContext actualFlowContext), WeakReference<IRoutableViewModel>>> cachedItems =
            _viewModelCache.ToList();

        foreach (KeyValuePair< (MembershipViewType viewType, AuthenticationFlowContext actualFlowContext), WeakReference<IRoutableViewModel>> item in cachedItems)
        {
            if (!item.Value.TryGetTarget(out IRoutableViewModel? viewModel))
            {
                continue;
            }

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
                _ = Task.Run(async () =>
                {
                    Result<Utilities.Unit, InternalServiceApiFailure> result =
                        await _applicationSecureStorageProvider.SetApplicationSettingsCultureAsync(targetCulture)
                            .ConfigureAwait(false);
                    if (result.IsErr)
                    {
                        Log.Warning(
                            "[LANGUAGE-CHANGE] Failed to persist culture setting. Culture: {Culture}, Error: {Error}",
                            targetCulture, result.UnwrapErr().Message);
                    }
                });
            });
    }

    private Task HandleBottomSheetDismissedEvent(BottomSheetHiddenEvent evt)
    {
        _languageSubscription?.Dispose();
        _bottomSheetHiddenSubscription?.Dispose();
        return Task.CompletedTask;
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

                    DetectLanguageDialog detectLanguageView = new() { DataContext = detectLanguageViewModel };

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

    private static readonly FrozenSet<MembershipViewType> FlowSpecificViews = new HashSet<MembershipViewType>
    {
        MembershipViewType.MobileVerification,
        MembershipViewType.VerifyOtp,
        MembershipViewType.ConfirmSecureKey,
    }.ToFrozenSet();


    private IRoutableViewModel GetOrCreateViewModelForView(MembershipViewType viewType, bool resetState = true)
    {
        AuthenticationFlowContext flowContext = CurrentFlowContext;

        bool useFlowSpecificCaching = FlowSpecificViews.Contains(viewType);

        (MembershipViewType viewType, AuthenticationFlowContext) cacheKey = useFlowSpecificCaching
            ? (viewType, flowContext)
            : (viewType, AuthenticationFlowContext.Registration);

        if (_viewModelCache.TryGetValue(cacheKey, out WeakReference<IRoutableViewModel>? weakRef) &&
            weakRef.TryGetTarget(out IRoutableViewModel? cachedViewModel))
        {
            if (resetState && cachedViewModel is IResettable resettable)
            {
                resettable.ResetState();
            }
            return cachedViewModel;
        }

        if (!ViewModelFactories.TryGetValue(viewType, out Func<IConnectivityService, NetworkProvider, ILocalizationService, IAuthenticationService, IApplicationSecureStorageProvider, AuthenticationViewModel, IOpaqueRegistrationService, IPasswordRecoveryService, AuthenticationFlowContext, IRoutableViewModel> factory))
        {
            throw new InvalidOperationException($"No factory found for view type: {viewType}");
        }

        IRoutableViewModel newViewModel = factory(_connectivityService, _networkProvider,
            LocalizationService,
            _authenticationService, _applicationSecureStorageProvider, this, _opaqueRegistrationService,
            _passwordRecoveryService, flowContext);

        _viewModelCache[cacheKey] = new WeakReference<IRoutableViewModel>(newViewModel);

        if (resetState && newViewModel is IResettable resettableNew)
        {
            resettableNew.ResetState();
        }

        return newViewModel;
    }
}
