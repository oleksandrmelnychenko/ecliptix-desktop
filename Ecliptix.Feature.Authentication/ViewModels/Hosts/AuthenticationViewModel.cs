using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Messaging.Core.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Connectivity;
using Ecliptix.Core.Messaging.Core.Messaging.Events;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Authentication;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.ViewModels.Registration;
using Ecliptix.Feature.Authentication.ViewModels.SignIn;
using Ecliptix.Feature.Authentication.ViewModels.Welcome;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Network.Services.Common;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using ReactiveUI;
using Serilog;
using Splat;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;
using MembershipViewType = Ecliptix.Core.Modularity.Authentication.MembershipViewType;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Feature.Authentication.ViewModels.Hosts;

public sealed class AuthenticationViewModel : Core.MVVM.ViewModelBase, IAuthenticationHost
{
    private static readonly AppCultureSettings LanguageConfig = AppCultureSettings.Default;

    private static readonly FrozenDictionary<MembershipViewType, Func<ViewModelFactoryContext, IRoutableViewModel>>
        ViewModelFactories = new Dictionary<MembershipViewType, Func<ViewModelFactoryContext, IRoutableViewModel>>
        {
            [MembershipViewType.SIGN_IN_VIEW] = ctx =>
                new SignInViewModel(ctx.ConnectivityService, ctx.NetworkProvider, ctx.LocalizationService,
                    ctx.AuthRepository, ctx.HostViewModel, ctx.GlobalModalService, ctx.MessageBus),
            [MembershipViewType.WELCOME_VIEW] = ctx =>
                new WelcomeViewModel(ctx.HostViewModel, ctx.LocalizationService, ctx.NetworkProvider, ctx.GlobalModalService),
            [MembershipViewType.WELCOME_BACK_VIEW] = ctx =>
                new WelcomeBackViewModel(ctx.HostViewModel, ctx.LocalizationService, ctx.NetworkProvider, ctx.GlobalModalService, ctx.StorageProvider),
            [MembershipViewType.MOBILE_VERIFICATION_VIEW] = ctx =>
                new MobileVerificationViewModel(ctx.ConnectivityService, ctx.NetworkProvider, ctx.LocalizationService,
                    ctx.HostViewModel, ctx.StorageProvider, ctx.AuthRepository,
                    ctx.FlowContext, ctx.Settings, ctx.GlobalModalService, ctx.MessageBus),
            [MembershipViewType.SECURE_KEY_CONFIRMATION_VIEW] = ctx =>
                new SecureKeyConfirmationViewModel(ctx.ConnectivityService, ctx.NetworkProvider, ctx.LocalizationService,
                    ctx.HostViewModel, ctx.StorageProvider, ctx.AuthRepository,
                    ctx.FlowContext, ctx.GlobalModalService),
            [MembershipViewType.COMPLETE_PROFILE_VIEW] = ctx =>
                new CompleteProfileViewModel(ctx.ConnectivityService, ctx.NetworkProvider, ctx.LocalizationService, ctx.HostViewModel, ctx.StorageProvider, ctx.GlobalModalService),
            [MembershipViewType.PIN_SET_VIEW] = ctx =>
                new PassPhaseViewModel(ctx.LocalizationService, ctx.HostViewModel, ctx.NetworkProvider, ctx.GlobalModalService),
        }.ToFrozenDictionary();

    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IConnectivityService _connectivityService;
    private readonly NetworkProvider _networkProvider;
    private readonly ILanguageDetectionService _languageDetectionService;
    private readonly ILocalizationService _localizationService;
    private readonly IAuthRepository _authRepository;
    private readonly IGlobalModalService _globalModalService;
    private readonly DefaultSystemSettings _settings;
    private readonly IMessageBus _mesageBus;

    private readonly
        Dictionary<(MembershipViewType ViewType, AuthenticationFlowContext FlowContext),
            WeakReference<IRoutableViewModel>> _viewModelCache = new();

    private readonly Stack<IRoutableViewModel> _navigationStack = new();

    private IDisposable? _languageSubscription;
    private IDisposable? _modalHiddenSubscription;
    private IRoutableViewModel? _currentView;

    public AuthenticationFlowContext CurrentFlowContext { get; set; } = AuthenticationFlowContext.REGISTRATION;

    public AuthenticationViewModel(AuthenticationViewModelDependencies dependencies)
        : base(dependencies.NetworkProvider, dependencies.LocalizationService, dependencies.GlobalModalService)
    {
        _localizationService = dependencies.LocalizationService;
        _connectivityService = dependencies.ConnectivityService;
        _applicationSecureStorageProvider = dependencies.StorageProvider;
        _networkProvider = dependencies.NetworkProvider;
        _authRepository = dependencies.AuthRepository;
        _languageDetectionService = dependencies.LanguageDetectionService;
        _globalModalService = dependencies.GlobalModalService;
        _settings = dependencies.Settings;
        _mesageBus = dependencies.MessageBus;

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
        get;
        private set => this.RaiseAndSetIfChanged(ref field, value);
    }

    public RoutingState Router => new();

    public string AppVersion { get; private set; } = string.Empty;

    public string FullVersionInfo { get; private set; } = string.Empty;

    public string? RegistrationMobileNumber { get; set; }

    public string? RecoveryMobileNumber { get; set; }

    public ReactiveCommand<MembershipViewType, IRoutableViewModel> Navigate { get; private set; } = null!;

    public ReactiveCommand<Unit, IRoutableViewModel?> NavigateBack { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> SwitchToMainWindowCommand { get; private set; } = null!;

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
                MembershipViewType.WELCOME_VIEW,
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
        CurrentFlowContext = AuthenticationFlowContext.SECURE_KEY_RECOVERY;
        Navigate.Execute(MembershipViewType.MOBILE_VERIFICATION_VIEW).Subscribe();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CleanupAuthenticationFlow();
        }

        base.Dispose(disposing);
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
                case LanguageDetectionAction.CONFIRM when !string.IsNullOrEmpty(evt.TargetCulture):
                    ChangeApplicationLanguage(evt.TargetCulture);
                    break;

                case LanguageDetectionAction.DECLINE:
                    break;
            }

            await _globalModalService.CloseAllAsync().ConfigureAwait(false);
        }
        finally
        {
            _languageSubscription?.Dispose();
            _modalHiddenSubscription?.Dispose();
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

    private Task HandleModalDismissedEvent(ModalHiddenEvent evt)
    {
        _languageSubscription?.Dispose();
        _modalHiddenSubscription?.Dispose();
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
                        SubscriptionLifetime.SCOPED);
                _modalHiddenSubscription =
                    _globalModalService.OnModalHidden(HandleModalDismissedEvent,
                        SubscriptionLifetime.SCOPED);

                string currentCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;

                string expectedCulture = LanguageConfig.GetCultureByCountry(applicationInstanceSettings.Country);

                if (!string.Equals(currentCulture, expectedCulture, StringComparison.OrdinalIgnoreCase))
                {
                    DetectLanguageDialogViewModel detectLanguageViewModel = new(
                        LocalizationService,
                        _languageDetectionService,
                        _networkProvider
                    );

                    await _globalModalService.ShowBottomAsync(
                        detectLanguageViewModel,
                        showScrim: true,
                        isDismissable: true
                    ).ConfigureAwait(false);
                }
            }
        }
    }

    private static readonly FrozenSet<MembershipViewType> FlowSpecificViews = new HashSet<MembershipViewType>
    {
        MembershipViewType.MOBILE_VERIFICATION_VIEW,
        MembershipViewType.OTP_VERIFICATION_VIEW,
        MembershipViewType.SECURE_KEY_CONFIRMATION_VIEW,
        MembershipViewType.COMPLETE_PROFILE_VIEW
    }.ToFrozenSet();

    private IRoutableViewModel GetOrCreateViewModelForView(MembershipViewType viewType, bool resetState = true)
    {
        AuthenticationFlowContext flowContext = CurrentFlowContext;

        bool useFlowSpecificCaching = FlowSpecificViews.Contains(viewType);

        (MembershipViewType viewType, AuthenticationFlowContext) cacheKey = useFlowSpecificCaching
            ? (viewType, flowContext)
            : (viewType, AuthenticationFlowContext.REGISTRATION);

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
            GlobalModalService = _globalModalService,
            AuthRepository = _authRepository,
            StorageProvider = _applicationSecureStorageProvider,
            HostViewModel = this,
            FlowContext = flowContext,
            Settings = _settings,
            MessageBus = _mesageBus
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
                return;
            }

            Option<IModule> mainModuleOption = await moduleManager.LoadModuleAsync("Main");

            if (!mainModuleOption.IsSome)
            {
                return;
            }

            CleanupAuthenticationFlow();
            await router.NavigateToMainAsync();
        });

        OpenSupportCommand = ReactiveCommand.Create(() =>
        {
            bool success = BrowserHelper.OpenUrl(_settings.SupportUrl);
            if (!success)
            {
                Log.Warning("Failed to open privacy policy URL: {Url}", _settings.SupportUrl);
            }
        });
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

    public IRoutableViewModel? ExecuteNavigateBack()
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
        Log.Information("[AUTH-VM] SetupActivationBehavior called, registering WhenActivated");

        this.WhenActivated(disposables =>
        {
            Log.Information("[AUTH-VM] ✅ WhenActivated TRIGGERED! Activation successful!");

            _connectivityService.OnManualRetryRequested(HandleManualRetryRequestedAsync)
                .DisposeWith(disposables);
            Observable.Timer(TimeSpan.FromSeconds(2), RxApp.MainThreadScheduler)
                .SelectMany(_ => CheckCountryCultureMismatchCommand.Execute())
                .Subscribe(_ => { })
                .DisposeWith(disposables);
            Navigate.Execute(MembershipViewType.WELCOME_VIEW)
                .Subscribe(_ => { })
                .DisposeWith(disposables);
        });

        Log.Information("[AUTH-VM] WhenActivated registered, waiting for activation...");
    }

    private async Task HandleManualRetryRequestedAsync(ManualRetryRequestedEvent e)
    {
        Result<Ecliptix.Utilities.Unit, NetworkFailure> recoveryResult =
            await _networkProvider.ForceFreshConnectionAsync();

        if (recoveryResult.IsOk)
        {
            ConnectivityIntent intent =
                ConnectivityIntent.Connected(e.ConnectId, ConnectivityReason.MANUAL_RETRY)
                    with
                {
                    Source = ConnectivitySource.MANUAL_ACTION
                };
            await _connectivityService.PublishAsync(intent);
        }
    }
}
