using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;
using Keys = Ecliptix.Feature.Authentication.Services.Authentication.Constants.AuthenticationConstants.MobileVerificationKeys;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;

public sealed partial class MobileVerificationViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IAuthRepository _authRepository;
    private readonly AuthenticationFlowContext _flowContext;
    private readonly IConnectivityService _connectivityService;
    private readonly CompositeDisposable _disposables = new();
    private readonly DefaultSystemSettings _settings;
    private readonly IMessageBus? _messageBus;
    private readonly MobileVerificationFlowCoordinator _flowCoordinator;

    private CancellationTokenSource? _cancellationTokenSource;
    private bool _hasMobileNumberBeenTouched;
    private bool _isDisposed;
    private bool _hasManualCountrySelection;
    private const int CURRENT_STEP = 1;
    private string CountryPickerContext => $"MobileVerification.{_flowContext}";

    private readonly Subject<string> _executionErrorSubject = new();
    public IObservable<string> ExecutionError => _executionErrorSubject.AsObservable();

    public MobileVerificationViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IAuthRepository authRepository,
        AuthenticationFlowContext flowContext,
        DefaultSystemSettings settings,
        IGlobalModalService globalModalService,
        IMessageBus messageBus) : base(networkProvider, localizationService, globalModalService,
        connectivityService)
    {
        _authRepository = authRepository;
        _connectivityService = connectivityService;
        _flowContext = flowContext;
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _settings = settings;
        _messageBus = messageBus;
        _flowCoordinator = new MobileVerificationFlowCoordinator(_authRepository, _applicationSecureStorageProvider,
            localizationService);

        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);

        SetupSubscriptions();
        AttemptAutoSwitchCountry();
    }

    public string UrlPathSegment => "/mobile-verification";
    public IScreen HostScreen { get; }

    private string Localize(string registrationKey, string recoveryKey) =>
        GetSecureKeyLocalization(_flowContext, registrationKey, recoveryKey);

    public string StepBadgeText => _flowContext switch
    {
        AuthenticationFlowContext.REGISTRATION => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_STEPS),
        AuthenticationFlowContext.SECURE_KEY_RECOVERY => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_RECOVERY_STEPS),
        _ => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_STEPS)
    };

    public string Title => Localize(Keys.REGISTRATION_TITLE, Keys.RECOVERY_TITLE);

    public string Description => Localize(Keys.REGISTRATION_DESCRIPTION, Keys.RECOVERY_DESCRIPTION);

    public string Hint => Localize(Keys.REGISTRATION_HINT, Keys.RECOVERY_HINT);

    public string Watermark => Localize(Keys.REGISTRATION_WATERMARK, Keys.RECOVERY_WATERMARK);

    public string ButtonText => Localize(Keys.REGISTRATION_BUTTON, Keys.RECOVERY_BUTTON);

    public ReactiveCommand<Unit, Unit>? VerifyMobileNumberCommand { get; private set; }

    public ReactiveCommand<Unit, Unit>? OpenCountryPickerCommand { get; private set; }

    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; private set; } = null!;

    public ReactiveCommand<Unit, Unit> OpenTermsOfServiceCommand { get; private set; } = null!;

    [Reactive] public string RawMobileNumber { get; set; } = string.Empty;
    [Reactive] public string? MobileNumberError { get; private set; }
    [Reactive] public bool HasMobileNumberError { get; private set; }

    [Reactive] public string CountryFlag { get; set; } = AppCultureSettingsConstants.UNITED_STATES_FLAG_PATH;
    [Reactive] public string PhonePrefix { get; set; } = AppCultureSettingsConstants.UNITED_STATES_PHONE_PREFIX;
    [Reactive] public string CountryIso { get; set; } = AppCultureSettingsConstants.UNITED_STATES_COUNTRY_CODE;

    [ObservableAsProperty] public bool IsBusy { get; }
}
