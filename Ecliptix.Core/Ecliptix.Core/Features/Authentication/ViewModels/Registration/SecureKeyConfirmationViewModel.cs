using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Core.Localization;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Keys = Ecliptix.Core.Services.Authentication.Constants.AuthenticationConstants.SecureKeyConfirmationKeys;
using SystemU = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public sealed class RequirementItem : ReactiveObject
{
    [Reactive] public string Text { get; set; }
    [Reactive] public bool IsMet { get; set; }

    public RequirementItem(string text, bool isMet)
    {
        Text = text;
        IsMet = isMet;
    }
}

public sealed class SecureKeyConfirmationViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private const int VALIDATION_THROTTLE_MS = 150;

    private const int CURRENT_STEP = 3;

    private readonly SecureTextBuffer _secureKeyBuffer = new();
    private readonly SecureTextBuffer _verifySecureKeyBuffer = new();
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly IAuthenticationService _authenticationService;
    private readonly ISecureKeyRecoveryService _secureKeyRecoveryService;
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
        IOpaqueRegistrationService registrationService,
        IAuthenticationService authenticationService,
        ISecureKeyRecoveryService secureKeyRecoveryService,
        AuthenticationFlowContext flowContext,
        IGlobalModalService globalModalService
    ) : base(networkProvider, localizationService, globalModalService,connectivityService)
    {
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _registrationService = registrationService;
        _authenticationService = authenticationService;
        _secureKeyRecoveryService = secureKeyRecoveryService;
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

    private void SetServerError(string? error)
    {
        string message = error ?? string.Empty;

        _executionErrorSubject.OnNext(message);

        ServerError = message;
        HasServerError = !string.IsNullOrEmpty(message);
    }

    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(
                x => x.IsBusy,
                x => x.IsInNetworkOutage,
                x => x.IsMembershipLoading,
                (isBusy, isInOutage, isMembershipLoading) =>
                {
                    bool canExecute = !isBusy && !isInOutage && !isMembershipLoading;
                    return canExecute;
                })
            .CombineLatest(isFormLogicallyValid, (canExecute, isValid) =>
            {
                bool finalResult = canExecute && isValid;
                return finalResult;
            });

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitAsync, canExecuteSubmit);
        SubmitCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);
        canExecuteSubmit.ToPropertyEx(this, x => x.CanSubmit);
    }

    private void SetupSubscriptions()
    {
    // TODO commmented for a test purposes
        this.WhenActivated(disposables =>
        {
            Observable.FromAsync(async () =>
                {
                    Result<Unit, InternalServiceApiFailure> result = await LoadMembershipAsync();

                    IsMembershipLoading = false;

                    if (result.IsErr)
                    {
                        await HandleMissingMembershipAsync(result.UnwrapErr().Message);
                    }
                })
                .Subscribe()
                .DisposeWith(disposables);
        });
    }

    private async Task HandleMissingMembershipAsync(string errorMessage)
    {
        string title = LocalizationService[LocalizationKeys.Authentication.WelcomeBack.ERROR_TITLE]
                       ?? "Error";
        string subtitle = LocalizationService[LocalizationKeys.Authentication.WelcomeBack.ERROR_SUBTITLE]
                          ?? "Session Error";
        string message = LocalizationService[LocalizationKeys.Authentication.WelcomeBack.ERROR_SESSION_MISSING];

        if (string.IsNullOrEmpty(message))
        {
            message = !string.IsNullOrEmpty(errorMessage)
                ? errorMessage
                : "Critical session data missing.";
        }

        await StartAutoRedirectSequenceAsync(
            HostScreen,
            message,
            10,
            (host) =>
            {
                if (host is { } authVm)
                {
                    authVm.ClearNavigationStack();
                    authVm.Navigate.Execute(MembershipViewType.WELCOME_VIEW).Subscribe();
                }
            },
            title,
            subtitle
        );
    }

    public void InsertSecureKeyChars(int index, string chars)
    {
        if (!_hasSecureKeyBeenTouched)
        {
            _hasSecureKeyBeenTouched = true;
        }

        _secureKeyBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentSecureKeyLength));
    }

    public void RemoveSecureKeyChars(int index, int count)
    {
        if (!_hasSecureKeyBeenTouched)
        {
            _hasSecureKeyBeenTouched = true;
        }

        _secureKeyBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentSecureKeyLength));
    }

    public void InsertVerifySecureKeyChars(int index, string chars)
    {
        if (!_hasVerifySecureKeyBeenTouched)
        {
            _hasVerifySecureKeyBeenTouched = true;
        }

        _verifySecureKeyBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentVerifySecureKeyLength));
    }

    public void RemoveVerifySecureKeyChars(int index, int count)
    {
        if (!_hasVerifySecureKeyBeenTouched)
        {
            _hasVerifySecureKeyBeenTouched = true;
        }

        _verifySecureKeyBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentVerifySecureKeyLength));
    }

    public async Task HandleEnterKeyPressAsync()
    {
        if (await SubmitCommand.CanExecute.FirstOrDefaultAsync())
        {
            SubmitCommand.Execute().Subscribe();
        }
    }

    public void ResetState()
    {
        _hasSecureKeyBeenTouched = false;
        _hasVerifySecureKeyBeenTouched = false;

        _secureKeyBuffer.Remove(0, _secureKeyBuffer.Length);
        _verifySecureKeyBuffer.Remove(0, _verifySecureKeyBuffer.Length);

        this.RaisePropertyChanged(nameof(CurrentSecureKeyLength));
        this.RaisePropertyChanged(nameof(CurrentVerifySecureKeyLength));

        SecureKeyError = string.Empty;
        HasSecureKeyError = false;
        VerifySecureKeyError = string.Empty;
        HasVerifySecureKeyError = false;

        ServerError = string.Empty;
        HasServerError = false;

        IsMembershipLoading = true;
        MembershipUniqueId = null;

        if (ValidationTips != null)
        {
            foreach (RequirementItem tip in ValidationTips)
            {
                tip.IsMet = false;
            }
        }

        SetServerError(string.Empty);
    }

    private string Localize(string registrationKey, string recoveryKey) =>
        GetSecureKeyLocalization(_flowContext, registrationKey, recoveryKey);

    private async Task<Result<Unit, InternalServiceApiFailure>> LoadMembershipAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> applicationInstance =
            await _applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync();

        if (applicationInstance.IsErr)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(applicationInstance.UnwrapErr());
        }

        ApplicationInstanceSettings settings = applicationInstance.Unwrap();

        if (settings.Membership == null)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound(
                    "Membership data is not available. Please complete registration from the beginning."));
        }

        if (settings.Membership.UniqueIdentifier == null || settings.Membership.UniqueIdentifier.IsEmpty)
        {
            return Result<Unit, InternalServiceApiFailure>.Err(
                InternalServiceApiFailure.SecureStoreKeyNotFound(
                    "Membership unique identifier is missing. Please complete registration from the beginning."));
        }

        MembershipUniqueId = settings.Membership.UniqueIdentifier;
        return Result<Unit, InternalServiceApiFailure>.Ok(Unit.Value);
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<SystemU> languageTrigger = LanguageChanged;

        IObservable<SystemU> lengthTrigger = this
            .WhenAnyValue(x => x.CurrentSecureKeyLength, x => x.CurrentVerifySecureKeyLength)
            .Do(lengths => Serilog.Log.Information(
                "[SECURE-KEY-VM] Key lengths changed: SecureKey={SecureKeyLength}, VerifyKey={VerifyKeyLength}",
                lengths.Item1, lengths.Item2))
            .Select(_ => SystemU.Default);

        IObservable<SystemU> validationTrigger = lengthTrigger.Merge(languageTrigger);

        IObservable<bool> isSecureKeyLogicallyValid = SetupSecureKeyValidation(validationTrigger);
        IObservable<bool> secureKeysMatch = SetupVerifyKeyValidation(validationTrigger);

        return isSecureKeyLogicallyValid
            .CombineLatest(secureKeysMatch, (isSecureKeyValid, areMatching) =>
            {
                bool result = isSecureKeyValid && areMatching;
                return result;
            })
            .DistinctUntilChanged();
    }

    private IObservable<bool> SetupSecureKeyValidation(IObservable<SystemU> validationTrigger)
    {
        IObservable<(string? ERROR, List<(string Text, bool IsMet)> Checklist, SecureKeyStrength Strength, bool IsSuccess)> validationResult = validationTrigger
            .StartWith(SystemU.Default)
            .Select(_ => ValidateSecureKeyInternal())
            .Replay(1)
            .RefCount();

        validationResult
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(v =>
            {
                for (int i = 0; i < v.Checklist.Count && i < ValidationTips.Count; i++)
                {
                    ValidationTips[i].IsMet = v.Checklist[i].IsMet;
                    ValidationTips[i].Text = v.Checklist[i].Text;
                }
            })
            .DisposeWith(_disposables);

        validationResult.Select(v => v.IsSuccess).ToPropertyEx(this, x => x.IsSecureKeySuccess);
        validationResult.Select(v => v.Strength).ToPropertyEx(this, x => x.CurrentSecureKeyStrength);

        validationResult.Select(v =>
        {
            if (!string.IsNullOrEmpty(v.ERROR))
            {
                return v.ERROR;
            }

            return _hasSecureKeyBeenTouched ? FormatSecureKeyStrengthMessage(v.Strength, null, null) : string.Empty;
        }).ToPropertyEx(this, x => x.SecureKeyStrengthMessage);

        this.WhenAnyValue(x => x.CurrentSecureKeyLength)
            .Select(_ => _hasSecureKeyBeenTouched)
            .ToPropertyEx(this, x => x.HasSecureKeyBeenTouched);

        this.WhenAnyValue(x => x.SecureKeyStrengthMessage)
            .Subscribe(m => SecureKeyError = m)
            .DisposeWith(_disposables);

        return validationResult.Select(v => v.IsSuccess);
    }

    private IObservable<bool> SetupVerifyKeyValidation(IObservable<SystemU> validationTrigger)
    {
        IObservable<bool> secureKeysMatch = validationTrigger
            .Select(_ => DoSecureKeysMatch())
            .Replay(1)
            .RefCount();

        IObservable<string> verifySecureKeyErrorStream = secureKeysMatch
            .Select(match =>
            {
                bool shouldShowError = _hasVerifySecureKeyBeenTouched && !match;
                return shouldShowError
                    ? LocalizationService[AuthenticationConstants.VERIFY_SECURE_KEY_DOES_NOT_MATCH_KEY]
                    : string.Empty;
            })
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(VALIDATION_THROTTLE_MS))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();

        verifySecureKeyErrorStream
            .Subscribe(error => VerifySecureKeyError = error)
            .DisposeWith(_disposables);
        this.WhenAnyValue(x => x.VerifySecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasVerifySecureKeyError = flag)
            .DisposeWith(_disposables);

        return secureKeysMatch;
    }

    private (string? ERROR, List<(string Text, bool IsMet)> Checklist, SecureKeyStrength Strength, bool IsSuccess) ValidateSecureKeyInternal()
    {
        string? error = null;
        List<(string Description, bool IsMet)> checklist = new();
        SecureKeyStrength strength = SecureKeyStrength.INVALID;
        bool isSuccess = false;

        _secureKeyBuffer.WithSecureBytes(bytes =>
        {
            string secureKey = Encoding.UTF8.GetString(bytes);
            checklist = SecureKeyValidator.GetChecklistStatus(secureKey, LocalizationService);
            strength = SecureKeyValidator.EstimateSecureKeyStrength(secureKey, LocalizationService);
            isSuccess = checklist.All(x => x.IsMet);
        });

        return (error, checklist, strength, isSuccess);
    }

    private bool DoSecureKeysMatch()
    {
        if (_secureKeyBuffer.Length != _verifySecureKeyBuffer.Length)
        {
            return false;
        }

        if (_secureKeyBuffer.Length == 0)
        {
            return true;
        }

        int length = _secureKeyBuffer.Length;
        byte[] secureKeyArray = ArrayPool<byte>.Shared.Rent(length);
        byte[] verifyArray = ArrayPool<byte>.Shared.Rent(length);

        try
        {
            _secureKeyBuffer.WithSecureBytes(secureKeyBytes => { secureKeyBytes.CopyTo(secureKeyArray.AsSpan()); });
            _verifySecureKeyBuffer.WithSecureBytes(verifyBytes => { verifyBytes.CopyTo(verifyArray.AsSpan()); });

            return CryptographicOperations.FixedTimeEquals(
                secureKeyArray.AsSpan(0, length),
                verifyArray.AsSpan(0, length));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(secureKeyArray, clearArray: true);
            ArrayPool<byte>.Shared.Return(verifyArray, clearArray: true);
        }
    }

    private string FormatSecureKeyStrengthMessage(SecureKeyStrength strength, string? error, string? recommendations)
    {
        string strengthText = strength switch
        {
            SecureKeyStrength.INVALID => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_INVALID_KEY],
            SecureKeyStrength.VERY_WEAK => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_VERY_WEAK_KEY],
            SecureKeyStrength.WEAK => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_WEAK_KEY],
            SecureKeyStrength.GOOD => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_GOOD_KEY],
            SecureKeyStrength.STRONG => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_STRONG_KEY],
            SecureKeyStrength.VERY_STRONG => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_VERY_STRONG_KEY],
            _ => LocalizationService[AuthenticationConstants.SECURE_KEY_STRENGTH_INVALID_KEY]
        };

        string message = !string.IsNullOrEmpty(error) ? error : (recommendations ?? string.Empty);
        return string.IsNullOrEmpty(message) ? strengthText : $"{strengthText}: {message}";
    }

    private async Task<SystemU> SubmitAsync()
    {
        if (IsBusy || !CanSubmit)
        {
            return SystemU.Default;
        }

        try
        {
            CancellationTokenSource operationCts = RecreateCancellationToken(ref _currentOperationCts);
            CancellationToken operationToken = operationCts.Token;

            try
            {
                uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

                Task<Result<Unit, string>> completeTask = _flowContext == AuthenticationFlowContext.REGISTRATION
                    ? CompleteRegistrationAsync(connectId, operationToken)
                    : CompleteSecureKeyResetAsync(connectId, operationToken);

                Result<Unit, string> result = await completeTask;

                if (result.IsErr)
                {
                    SetServerError(result.UnwrapErr());
                    return SystemU.Default;
                }

                if (HostScreen is not AuthenticationViewModel hostViewModel)
                {
                    return SystemU.Default;
                }

                if (_flowContext == AuthenticationFlowContext.REGISTRATION)
                {
                    Option<string> mobileNumberOpt = Option<string>.Some(hostViewModel.RegistrationMobileNumber!);

                    if (mobileNumberOpt.IsSome)
                    {
                        bool signedIn = await SignInAsync(mobileNumberOpt.Value!, connectId, operationToken, navigateToMain: false);

                        if (signedIn)
                        {
                            hostViewModel.Navigate.Execute(MembershipViewType.COMPLETE_PROFILE_VIEW).Subscribe();
                        }
                    }
                    //TODO we are currently signin in, after restart we are gonna be authenticated, we should provide new statuses
                    //TODO disable navigation back
                    //TODO test logic on recovery
                    return SystemU.Default;
                }

                Option<string> mobileNumberOption = Option<string>.Some(hostViewModel.RecoveryMobileNumber!);

                if (mobileNumberOption.IsSome)
                {
                    await SignInAsync(mobileNumberOption.Value!, connectId, operationToken);
                }

                return SystemU.Default;
            }
            finally
            {
                if (_currentOperationCts != null && ReferenceEquals(_currentOperationCts, operationCts))
                {
                    _currentOperationCts = null;
                }

                operationCts.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            return SystemU.Default;
        }
    }

    private async Task<Result<Unit, string>> CompleteRegistrationAsync(uint connectId,
        CancellationToken cancellationToken)
    {
        if (MembershipUniqueId == null)
        {
            return Result<Unit, string>.Err(
                LocalizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]);
        }

        return await _registrationService.CompleteRegistrationAsync(
            MembershipUniqueId,
            _secureKeyBuffer,
            connectId,
            cancellationToken);
    }

    private async Task<Result<Unit, string>> CompleteSecureKeyResetAsync(uint connectId,
        CancellationToken cancellationToken)
    {
        if (MembershipUniqueId == null)
        {
            return Result<Unit, string>.Err(
                LocalizationService[AuthenticationConstants.MEMBERSHIP_IDENTIFIER_REQUIRED_KEY]);
        }

        return await _secureKeyRecoveryService.CompleteSecureKeyResetAsync(
            MembershipUniqueId,
            _secureKeyBuffer,
            connectId,
            cancellationToken);
    }

    private async Task<bool> SignInAsync(string mobileNumber, uint connectId, CancellationToken cancellationToken, bool navigateToMain = true)
    {
        Result<Unit, AuthenticationFailure> signInResult = await _authenticationService.SignInAsync(
            mobileNumber,
            _secureKeyBuffer,
            connectId,
            cancellationToken);

        if (signInResult.IsOk)
        {
            if (navigateToMain && HostScreen is AuthenticationViewModel hostViewModel)
            {
                try
                {
                    await hostViewModel.SwitchToMainWindowCommand.Execute();
                }
                catch
                {
                    SetServerError($"{LocalizationService[AuthenticationConstants.NAVIGATION_FAILURE_KEY]}");
                }
            }
            return true;
        }
        else if (signInResult.IsErr)
        {
            AuthenticationFailure failure = signInResult.UnwrapErr();
            SetServerError(failure.Message);
            return false;
        }

        return false;
    }
    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            CancelCurrentOperation();
            _secureKeyBuffer.Dispose();
            _verifySecureKeyBuffer.Dispose();
            _executionErrorSubject.Dispose();
            _disposables.Dispose();
        }

        _isDisposed = true;
        base.Dispose(disposing);
    }

    private void CancelCurrentOperation()
    {
        CancellationTokenSource? operationSource = Interlocked.Exchange(
            ref _currentOperationCts,
            null);

        if (operationSource == null)
        {
            return;
        }

        try
        {
            operationSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Intentionally suppressed: CancellationTokenSource already disposed of during cleanup
        }
        finally
        {
            operationSource.Dispose();
        }
    }
}
