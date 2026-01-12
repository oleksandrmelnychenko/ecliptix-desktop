using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Abstractions.Membership;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.Services.Authentication;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Authentication.AuthenticationFlowContext;
using Keys = Ecliptix.Feature.Authentication.Services.Authentication.Constants.AuthenticationConstants.SecureKeyConfirmationKeys;
using SecureKeyValidator = Ecliptix.Feature.Authentication.Services.Membership.SecureKeyValidator;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;

public sealed partial class SecureKeyConfirmationViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private const int VALIDATION_THROTTLE_MS = 150;
    private const int CURRENT_STEP = 3;

    private readonly SecureTextBuffer _secureKeyBuffer = new();
    private readonly SecureTextBuffer _verifySecureKeyBuffer = new();
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IAuthRepository _authRepository;
    private readonly AuthenticationFlowContext _flowContext;

    private readonly CompositeDisposable _disposables = new();
    private CancellationTokenSource? _currentOperationCts;
    private bool _hasSecureKeyBeenTouched;
    private bool _hasVerifySecureKeyBeenTouched;
    private bool _isDisposed;

    private readonly Subject<string> _executionErrorSubject = new();
    public IObservable<string> ExecutionError => _executionErrorSubject.AsObservable();

    public SecureKeyConfirmationViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IAuthRepository authRepository,
        AuthenticationFlowContext flowContext,
        IGlobalModalService globalModalService
    ) : base(networkProvider, localizationService, globalModalService, connectivityService)
    {
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _authRepository = authRepository;
        _flowContext = flowContext;

        ValidationTips = SecureKeyValidator.GetChecklistStatus(string.Empty, localizationService)
            .Select(x => new RequirementItem(x.Description, false))
            .ToList()
            .AsReadOnly();

        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
        SetupSubscriptions();
    }

    public string StepBadgeText => _flowContext switch
    {
        AuthenticationFlowContext.REGISTRATION => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_STEPS),
        AuthenticationFlowContext.SECURE_KEY_RECOVERY => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_RECOVERY_STEPS),
        _ => string.Format(StepFormatKey, CURRENT_STEP, TOTAL_STEPS)
    };

    public string Title => Localize(Keys.REGISTRATION_TITLE, Keys.RECOVERY_TITLE);
    public string Description => Localize(Keys.REGISTRATION_DESCRIPTION, Keys.RECOVERY_DESCRIPTION);
    public string SecureKeyPlaceholder => Localize(Keys.SECURE_KEY_PLACEHOLDER, Keys.RECOVERY_SECURE_KEY_PLACEHOLDER);
    public string SecureKeyHint => Localize(Keys.SECURE_KEY_HINT, Keys.RECOVERY_SECURE_KEY_HINT);

    public string VerifySecureKeyPlaceholder =>
        Localize(Keys.VERIFY_SECURE_KEY_PLACEHOLDER, Keys.RECOVERY_VERIFY_SECURE_KEY_PLACEHOLDER);

    public string VerifySecureKeyHint => Localize(Keys.VERIFY_SECURE_KEY_HINT, Keys.RECOVERY_VERIFY_SECURE_KEY_HINT);
    public string ButtonText => Localize(Keys.REGISTRATION_BUTTON, Keys.RECOVERY_BUTTON);

    public string RequirementsTitle => Localize(Keys.REQUIREMENTS_TITLE_KEY, Keys.REQUIREMENTS_TITLE_KEY);

    public string RequirementsSuccessTitle =>
        Localize(Keys.REQUIREMENTS_SUCCESS_TITLE_KEY, Keys.REQUIREMENTS_SUCCESS_TITLE_KEY);

    public string UrlPathSegment => "/secure-key-confirmation";
    public IScreen HostScreen { get; }

    public int CurrentSecureKeyLength => _secureKeyBuffer.Length;
    public int CurrentVerifySecureKeyLength => _verifySecureKeyBuffer.Length;

    public ReactiveCommand<SystemU, SystemU> SubmitCommand { get; private set; } = null!;

    [Reactive] public string? SecureKeyError { get; private set; }
    [Reactive] public bool HasSecureKeyError { get; private set; }
    [Reactive] public string? VerifySecureKeyError { get; private set; }
    [Reactive] public bool HasVerifySecureKeyError { get; private set; }
    [Reactive] public string? ServerError { get; private set; }
    [Reactive] public bool HasServerError { get; private set; }
    [ObservableAsProperty] public bool CanSubmit { get; }

    public IReadOnlyList<RequirementItem> ValidationTips { get; }
    [ObservableAsProperty] public bool IsSecureKeySuccess { get; private set; }
    [ObservableAsProperty] public SecureKeyStrength CurrentSecureKeyStrength { get; private set; }
    [ObservableAsProperty] public string? SecureKeyStrengthMessage { get; private set; }
    [ObservableAsProperty] public bool HasSecureKeyBeenTouched { get; private set; }
    [ObservableAsProperty] public bool IsBusy { get; }

    [Reactive] public bool IsMembershipLoading { get; private set; } = true;

    private ByteString? MembershipUniqueId { get; set; }

    private string Localize(string registrationKey, string recoveryKey) =>
        GetSecureKeyLocalization(_flowContext, registrationKey, recoveryKey);

    private void SetServerError(string? error)
    {
        string message = error ?? string.Empty;

        _executionErrorSubject.OnNext(message);

        ServerError = message;
        HasServerError = !string.IsNullOrEmpty(message);
    }
}
