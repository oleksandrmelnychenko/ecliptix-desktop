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

public readonly struct AuthenticationViewModelDependencies
{
    public required IConnectivityService ConnectivityService { get; init; }
    public required NetworkProvider NetworkProvider { get; init; }
    public required ILocalizationService LocalizationService { get; init; }
    public required IApplicationSecureStorageProvider StorageProvider { get; init; }
    public required IAuthenticationService AuthenticationService { get; init; }
    public required IOpaqueRegistrationService RegistrationService { get; init; }
    public required ISecureKeyRecoveryService RecoveryService { get; init; }
    public required ILanguageDetectionService LanguageDetectionService { get; init; }
    public required IApplicationRouter Router { get; init; }
    public required MainWindowViewModel MainWindowViewModel { get; init; }
    public required DefaultSystemSettings Settings { get; init; }
}

public readonly struct ViewModelFactoryContext
{
    public required IConnectivityService ConnectivityService { get; init; }
    public required NetworkProvider NetworkProvider { get; init; }
    public required ILocalizationService LocalizationService { get; init; }
    public required IAuthenticationService AuthenticationService { get; init; }
    public required IApplicationSecureStorageProvider StorageProvider { get; init; }
    public required AuthenticationViewModel HostViewModel { get; init; }
    public required IOpaqueRegistrationService RegistrationService { get; init; }
    public required ISecureKeyRecoveryService RecoveryService { get; init; }
    public required AuthenticationFlowContext FlowContext { get; init; }
}

public class AuthenticationViewModel : Core.MVVM.ViewModelBase, IScreen
{
    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    private static readonly FrozenDictionary<MembershipViewType, Func<ViewModelFactoryContext, IRoutableViewModel>>
        ViewModelFactories = new Dictionary<MembershipViewType, Func<ViewModelFactoryContext, IRoutableViewModel>>
        {
            [MembershipViewType.SignInView] = ctx =>
                new SignInViewModel(ctx.ConnectivityService, ctx.NetworkProvider, ctx.LocalizationService,
                    ctx.AuthenticationService, ctx.HostViewModel),
            [MembershipViewType.WelcomeView] = ctx =>
                new WelcomeViewModel(ctx.HostViewModel, ctx.LocalizationService, ctx.NetworkProvider),
            [MembershipViewType.MobileVerificationView] = ctx =>
                new MobileVerificationViewModel(ctx.ConnectivityService, ctx.NetworkProvider, ctx.LocalizationService,
                    ctx.HostViewModel, ctx.StorageProvider, ctx.RegistrationService,
                    ctx.RecoveryService, ctx.FlowContext),
            [MembershipViewType.SecureKeyConfirmationView] = ctx =>
                new SecureKeyVerifierViewModel(ctx.ConnectivityService, ctx.NetworkProvider, ctx.LocalizationService,
                    ctx.HostViewModel, ctx.StorageProvider, ctx.RegistrationService, ctx.AuthenticationService,
                    ctx.RecoveryService, ctx.FlowContext),
            [MembershipViewType.PinSetView] = ctx =>
                new PassPhaseViewModel(ctx.LocalizationService, ctx.HostViewModel, ctx.NetworkProvider),
        }.ToFrozenDictionary();

    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IConnectivityService _connectivityService;
    private readonly NetworkProvider _networkProvider;
    private readonly ILanguageDetectionService _languageDetectionService;
    private readonly ILocalizationService _localizationService;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private readonly IAuthenticationService _authenticationService;
    private readonly IOpaqueRegistrationService _opaqueRegistrationService;
    private readonly ISecureKeyRecoveryService _secureKeyRecoveryService;
    private readonly DefaultSystemSettings _settings;

    private readonly
        Dictionary<(MembershipViewType ViewType, AuthenticationFlowContext FlowContext),
            WeakReference<IRoutableViewModel>> _viewModelCache = new();

    private readonly Stack<IRoutableViewModel> _navigationStack = new();

    private bool _canNavigateBack;
    private IDisposable? _languageSubscription;
    private IDisposable? _bottomSheetHiddenSubscription;
    private IRoutableViewModel? _currentView;

    public AuthenticationFlowContext CurrentFlowContext { get; set; } = AuthenticationFlowContext.Registration;

    public AuthenticationViewModel(AuthenticationViewModelDependencies dependencies)
        : base(dependencies.NetworkProvider, dependencies.LocalizationService)
    {
        _localizationService = dependencies.LocalizationService;
        _connectivityService = dependencies.ConnectivityService;
        _applicationSecureStorageProvider = dependencies.StorageProvider;
        _networkProvider = dependencies.NetworkProvider;
        _authenticationService = dependencies.AuthenticationService;
        _opaqueRegistrationService = dependencies.RegistrationService;
        _secureKeyRecoveryService = dependencies.RecoveryService;
        _languageDetectionService = dependencies.LanguageDetectionService;
        _mainWindowViewModel = dependencies.MainWindowViewModel;
        _settings = dependencies.Settings;

        InitializeVersionInfo();
        InitializeCommands(dependencies.Router);
        SetupActivationBehavior();
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

    public string AppVersion { get; private set; } = string.Empty;

    public string FullVersionInfo { get; private set; } = string.Empty;

    public string? RegistrationMobileNumber { get; set; }

    public string? RecoveryMobileNumber { get; set; }

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; private set; } = null!;

    public ReactiveCommand<Unit, IRoutableViewModel?> NavigateBack { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> SwitchToMainWindowCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> OpenSupportCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> CheckCountryCultureMismatchCommand { get; private set; } = null!;

    public void ClearNavigationStack(bool preserveInitialWelcome = false, MembershipViewType? preserveViewType = null)
    {
        if (_currentView is IResettable currentResettable)
        {
            currentResettable.ResetState();
        }

        _navigationStack.Clear();

        if (preserveInitialWelcome)
        {
            IRoutableViewModel welcomeView = GetOrCreateViewModelForView(
                MembershipViewType.WelcomeView,
                resetState: true);
            _navigationStack.Push(welcomeView);
        }

        if (preserveViewType.HasValue)
        {
            IRoutableViewModel preservedView = GetOrCreateViewModelForView(
                preserveViewType.Value,
                resetState: true);
            _navigationStack.Push(preservedView);
        }

        _currentView = null;
        this.RaisePropertyChanged(nameof(CurrentView));

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

    public void StartSecureKeyRecoveryFlow()
    {
        ClearNavigationStack(true);
        CurrentFlowContext = AuthenticationFlowContext.SecureKeyRecovery;
        Navigate.Execute(MembershipViewType.MobileVerificationView).Subscribe();
    }

    public async Task ShowBottomSheet(BottomSheetComponentType componentType, UserControl redirectView,
        bool showScrim = true, bool isDismissable = false)
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
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
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
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

        List<KeyValuePair<(MembershipViewType viewType, AuthenticationFlowContext actualFlowContext),
            WeakReference<IRoutableViewModel>>> cachedItems =
            _viewModelCache.ToList();

        foreach (KeyValuePair<(MembershipViewType viewType, AuthenticationFlowContext actualFlowContext),
                     WeakReference<IRoutableViewModel>> item in cachedItems)
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
                Task.Run(async () =>
                {
                    await _applicationSecureStorageProvider.SetApplicationSettingsCultureAsync(targetCulture)
                        .ConfigureAwait(false);
                }).ContinueWith(
                    task =>
                    {
                        if (task is { IsFaulted: true, Exception: not null })
                        {
                            Log.Error(task.Exception, "[LANGUAGE-CHANGE] Unhandled exception persisting culture");
                        }
                    },
                    TaskScheduler.Default);
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
        MembershipViewType.MobileVerificationView,
        MembershipViewType.OtpVerificationView,
        MembershipViewType.SecureKeyConfirmationView,
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

        if (!ViewModelFactories.TryGetValue(viewType, out Func<ViewModelFactoryContext, IRoutableViewModel>? factory))
        {
            throw new InvalidOperationException($"No factory found for view type: {viewType}");
        }

        ViewModelFactoryContext context = new()
        {
            ConnectivityService = _connectivityService,
            NetworkProvider = _networkProvider,
            LocalizationService = LocalizationService,
            AuthenticationService = _authenticationService,
            StorageProvider = _applicationSecureStorageProvider,
            HostViewModel = this,
            RegistrationService = _opaqueRegistrationService,
            RecoveryService = _secureKeyRecoveryService,
            FlowContext = flowContext
        };

        IRoutableViewModel newViewModel = factory(context);

        _viewModelCache[cacheKey] = new WeakReference<IRoutableViewModel>(newViewModel);

        if (resetState && newViewModel is IResettable resettableNew)
        {
            resettableNew.ResetState();
        }

        return newViewModel;
    }

    private void InitializeVersionInfo()
    {
        AppVersion = VersionHelper.GetApplicationVersion();
        Option<BuildInfo> buildInfo = VersionHelper.GetBuildInfo();
        buildInfo.Select(bi => bi.BuildNumber).GetValueOrDefault("development");

        FullVersionInfo = buildInfo.Match(
            bi => string.Concat(
                VersionHelper.GetDisplayVersion(),
                "\nBuild: ", bi.BuildNumber,
                "\nCommit: ", bi.GitCommit[..8],
                "\nBranch: ", bi.GitBranch),
            VersionHelper.GetDisplayVersion);
    }

    private void InitializeCommands(IApplicationRouter router)
    {
        Navigate = ReactiveCommand.Create<MembershipViewType, IRoutableViewModel>(ExecuteNavigate);
        NavigateBack = ReactiveCommand.Create(ExecuteNavigateBack);
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

            Option<IModule> mainModuleOption = await moduleManager.LoadModuleAsync("Main");
            if (!mainModuleOption.IsSome)
            {
                Log.Error("[MEMBERSHIP-HOST] Failed to load Main module");
                return;
            }

            CleanupAuthenticationFlow();
            await router.NavigateToMainAsync();
        });
        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrl(_settings.PrivacyPolicyUrl));
        OpenTermsOfServiceCommand = ReactiveCommand.Create(() => OpenUrl(_settings.TermsOfServiceUrl));
        OpenSupportCommand = ReactiveCommand.Create(() => OpenUrl(_settings.SupportUrl));
    }

    private IRoutableViewModel ExecuteNavigate(MembershipViewType viewType)
    {
        IRoutableViewModel viewModel = GetOrCreateViewModelForView(viewType);

        if (_currentView != null)
        {
            _navigationStack.Push(_currentView);
        }

        CurrentView = viewModel;

        return viewModel;
    }

    private IRoutableViewModel? ExecuteNavigateBack()
    {
        if (_navigationStack.Count > 0)
        {
            if (_currentView is IResettable resettable)
            {
                resettable.ResetState();
            }

            IRoutableViewModel previousView = _navigationStack.Pop();

            if (previousView is IResettable previousResettable)
            {
                previousResettable.ResetState();
            }

            _currentView = previousView;
            this.RaisePropertyChanged(nameof(CurrentView));
            CanNavigateBack = _navigationStack.Count > 0;

            return previousView;
        }

        return null;
    }

    private void SetupActivationBehavior()
    {
        this.WhenActivated(disposables =>
        {
            _connectivityService.OnManualRetryRequested(HandleManualRetryRequestedAsync)
                .DisposeWith(disposables);
            Observable.Timer(TimeSpan.FromSeconds(2), RxApp.MainThreadScheduler)
                .SelectMany(_ => CheckCountryCultureMismatchCommand.Execute())
                .Subscribe(_ => { })
                .DisposeWith(disposables);
            Navigate.Execute(MembershipViewType.WelcomeView)
                .Subscribe(_ => { })
                .DisposeWith(disposables);
        });
    }

    private async Task HandleManualRetryRequestedAsync(ManualRetryRequestedEvent e)
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
    }
}
