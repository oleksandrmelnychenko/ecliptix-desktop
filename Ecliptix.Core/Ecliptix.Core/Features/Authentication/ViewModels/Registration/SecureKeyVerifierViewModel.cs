using System;
using System.Buffers;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
using Ecliptix.Core.Services.Membership;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using SystemU = System.Reactive.Unit;
using Keys = Ecliptix.Core.Services.Authentication.Constants.AuthenticationConstants.SecureKeyConfirmationKeys;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public sealed class SecureKeyVerifierViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable
{
    private const int ValidationThrottleMs = 150;

    private readonly SecureTextBuffer _secureKeyBuffer = new();
    private readonly SecureTextBuffer _verifySecureKeyBuffer = new();
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly IAuthenticationService _authenticationService;
    private readonly ISecureKeyRecoveryService _secureKeyRecoveryService;
    private readonly AuthenticationFlowContext _flowContext;

    private CancellationTokenSource? _currentOperationCts;
    private bool _hasSecureKeyBeenTouched;
    private bool _hasVerifySecureKeyBeenTouched;
    private bool _isDisposed;

    public SecureKeyVerifierViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        IAuthenticationService authenticationService,
        ISecureKeyRecoveryService secureKeyRecoveryService,
        AuthenticationFlowContext flowContext
    ) : base(networkProvider, localizationService, connectivityService)
    {
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _registrationService = registrationService;
        _authenticationService = authenticationService;
        _secureKeyRecoveryService = secureKeyRecoveryService;
        _flowContext = flowContext;

        Log.Information("[SECUREKEYVERIFIER-VM] Initialized with flow context: {FlowContext}", flowContext);

        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
        SetupSubscriptions();
    }

    public string Title => Localize(Keys.RegistrationTitle, Keys.RecoveryTitle);
    public string Description => Localize(Keys.RegistrationDescription, Keys.RecoveryDescription);
    public string SecureKeyPlaceholder => Localize(Keys.SecureKeyPlaceholder, Keys.RecoverySecureKeyPlaceholder);
    public string SecureKeyHint => Localize(Keys.SecureKeyHint, Keys.RecoverySecureKeyHint);

    public string VerifySecureKeyPlaceholder =>
        Localize(Keys.VerifySecureKeyPlaceholder, Keys.RecoveryVerifySecureKeyPlaceholder);

    public string VerifySecureKeyHint => Localize(Keys.VerifySecureKeyHint, Keys.RecoveryVerifySecureKeyHint);
    public string ButtonText => Localize(Keys.RegistrationButton, Keys.RecoveryButton);

    public string? UrlPathSegment { get; } = "/secure-key-confirmation";
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

    [ObservableAsProperty] public SecureKeyStrength CurrentSecureKeyStrength { get; private set; }
    [ObservableAsProperty] public string? SecureKeyStrengthMessage { get; private set; }
    [ObservableAsProperty] public bool HasSecureKeyBeenTouched { get; private set; }
    [ObservableAsProperty] public bool IsBusy { get; }

    private ByteString? MembershipUniqueId { get; set; }

    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(x => x.IsBusy, x => x.IsInNetworkOutage,
                (isBusy, isInOutage) => !isBusy && !isInOutage)
            .CombineLatest(isFormLogicallyValid, (canExecute, isValid) => canExecute && isValid);

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitAsync, canExecuteSubmit);
        SubmitCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);
        canExecuteSubmit.ToPropertyEx(this, x => x.CanSubmit);
    }

    private void SetupSubscriptions()
    {
        this.WhenActivated(disposables =>
        {
            Observable.FromAsync(LoadMembershipAsync)
                .Subscribe(result =>
                {
                    if (!result.IsErr)
                    {
                        return;
                    }

                    ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                    ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.Welcome);
                })
                .DisposeWith(disposables);

            this.WhenAnyValue(x => x.ServerError)
                .DistinctUntilChanged()
                .Subscribe(err
                    =>
                {
                    HasServerError = !string.IsNullOrEmpty(err);
                    if (!string.IsNullOrEmpty(err) && HostScreen is AuthenticationViewModel hostWindow)
                    {
                        ShowServerErrorNotification(hostWindow, err);
                    }
                })
                .DisposeWith(disposables);

            SubmitCommand
                .Where(_ => !IsBusy && CanSubmit)
                .Subscribe(_ =>
                {
                    ((AuthenticationViewModel)HostScreen).ClearNavigationStack();
                    ((AuthenticationViewModel)HostScreen).Navigate.Execute(MembershipViewType.PassPhase);
                })
                .DisposeWith(disposables);
        });
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
        _secureKeyBuffer.Remove(0, _secureKeyBuffer.Length);
        _verifySecureKeyBuffer.Remove(0, _verifySecureKeyBuffer.Length);
        _hasSecureKeyBeenTouched = false;
        _hasVerifySecureKeyBeenTouched = false;

        SecureKeyError = string.Empty;
        HasSecureKeyError = false;
        VerifySecureKeyError = string.Empty;
        HasVerifySecureKeyError = false;

        ServerError = string.Empty;
        HasServerError = false;
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
            .Select(_ => SystemU.Default);

        IObservable<SystemU> validationTrigger = lengthTrigger.Merge(languageTrigger);

        IObservable<bool> isSecureKeyLogicallyValid = SetupSecureKeyValidation(validationTrigger);
        IObservable<bool> secureKeysMatch = SetupVerifyKeyValidation(validationTrigger);

        return isSecureKeyLogicallyValid
            .CombineLatest(secureKeysMatch, (isSecureKeyValid, areMatching) => isSecureKeyValid && areMatching)
            .DistinctUntilChanged();
    }

    private IObservable<bool> SetupSecureKeyValidation(IObservable<SystemU> validationTrigger)
    {
        IObservable<(string? Error, string Recommendations, SecureKeyStrength Strength)> secureKeyValidation =
            validationTrigger
                .Select(_ => ValidateSecureKeyWithStrength())
                .Replay(1)
                .RefCount();

        secureKeyValidation.Select(v => v.Strength).ToPropertyEx(this, x => x.CurrentSecureKeyStrength);
        secureKeyValidation.Select(v =>
                _hasSecureKeyBeenTouched
                    ? FormatSecureKeyStrengthMessage(v.Strength, v.Error, v.Recommendations)
                    : string.Empty)
            .ToPropertyEx(this, x => x.SecureKeyStrengthMessage);

        this.WhenAnyValue(x => x.CurrentSecureKeyLength)
            .Select(_ => _hasSecureKeyBeenTouched)
            .ToPropertyEx(this, x => x.HasSecureKeyBeenTouched);

        this.WhenAnyValue(x => x.SecureKeyStrengthMessage)
            .Subscribe(message => SecureKeyError = message);
        this.WhenAnyValue(x => x.SecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasSecureKeyError = flag);

        return secureKeyValidation.Select(v => string.IsNullOrEmpty(v.Error));
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
                    ? LocalizationService[AuthenticationConstants.VerifySecureKeyDoesNotMatchKey]
                    : string.Empty;
            })
            .DistinctUntilChanged()
            .Throttle(TimeSpan.FromMilliseconds(ValidationThrottleMs))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Replay(1)
            .RefCount();

        verifySecureKeyErrorStream
            .DistinctUntilChanged()
            .Subscribe(error => VerifySecureKeyError = error);

        this.WhenAnyValue(x => x.VerifySecureKeyError)
            .Select(e => !string.IsNullOrEmpty(e))
            .Subscribe(flag => HasVerifySecureKeyError = flag);

        return secureKeysMatch;
    }

    private (string? Error, string Recommendations, SecureKeyStrength Strength) ValidateSecureKeyWithStrength()
    {
        string? error = null;
        string recommendations = string.Empty;
        SecureKeyStrength strength = SecureKeyStrength.Invalid;

        _secureKeyBuffer.WithSecureBytes(bytes =>
        {
            string secureKey = Encoding.UTF8.GetString(bytes);
            (error, List<string> recs) = SecureKeyValidator.Validate(secureKey, LocalizationService);
            strength = SecureKeyValidator.EstimateSecureKeyStrength(secureKey, LocalizationService);
            if (recs.Count > 0)
            {
                recommendations = recs[0];
            }
        });
        return (error, recommendations, strength);
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

    private string FormatSecureKeyStrengthMessage(SecureKeyStrength strength, string? error, string recommendations)
    {
        string strengthText = strength switch
        {
            SecureKeyStrength.Invalid => LocalizationService[AuthenticationConstants.SecureKeyStrengthInvalidKey],
            SecureKeyStrength.VeryWeak => LocalizationService[AuthenticationConstants.SecureKeyStrengthVeryWeakKey],
            SecureKeyStrength.Weak => LocalizationService[AuthenticationConstants.SecureKeyStrengthWeakKey],
            SecureKeyStrength.Good => LocalizationService[AuthenticationConstants.SecureKeyStrengthGoodKey],
            SecureKeyStrength.Strong => LocalizationService[AuthenticationConstants.SecureKeyStrengthStrongKey],
            SecureKeyStrength.VeryStrong => LocalizationService[AuthenticationConstants.SecureKeyStrengthVeryStrongKey],
            _ => LocalizationService[AuthenticationConstants.SecureKeyStrengthInvalidKey]
        };

        string message = !string.IsNullOrEmpty(error) ? error : recommendations;
        return string.IsNullOrEmpty(message) ? strengthText : $"{strengthText}: {message}";
    }

    private void SetServerError(string? error)
    {
        ServerError = error;
        HasServerError = !string.IsNullOrEmpty(error);
    }

    private async Task<SystemU> SubmitAsync()
    {
        if (IsBusy || !CanSubmit)
        {
            return SystemU.Default;
        }

        SetServerError(string.Empty);

        try
        {
            CancellationTokenSource operationCts = RecreateCancellationToken(ref _currentOperationCts);
            CancellationToken operationToken = operationCts.Token;

            try
            {
                uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

                Task<Result<Unit, string>> completeTask = _flowContext == AuthenticationFlowContext.Registration
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

                Option<string> mobileNumberOption = _flowContext == AuthenticationFlowContext.Registration
                    ? Option<string>.Some(hostViewModel.RegistrationMobileNumber!)
                    : Option<string>.Some(hostViewModel.RecoveryMobileNumber!);

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
                LocalizationService[AuthenticationConstants.MembershipIdentifierRequiredKey]);
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
                LocalizationService[AuthenticationConstants.MembershipIdentifierRequiredKey]);
        }

        return await _secureKeyRecoveryService!.CompleteSecureKeyResetAsync(
            MembershipUniqueId,
            _secureKeyBuffer,
            connectId,
            cancellationToken);
    }

    private async Task SignInAsync(string mobileNumber, uint connectId, CancellationToken cancellationToken)
    {
        Result<Unit, AuthenticationFailure> signInResult = await _authenticationService.SignInAsync(
            mobileNumber,
            _secureKeyBuffer,
            connectId,
            cancellationToken);

        if (signInResult.IsOk && HostScreen is AuthenticationViewModel hostViewModel)
        {
            try
            {
                await hostViewModel.SwitchToMainWindowCommand.Execute();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to switch to main window after successful authentication");
                SetServerError($"{LocalizationService[AuthenticationConstants.NavigationFailureKey]}");
            }
        }
        else if (signInResult.IsErr)
        {
            AuthenticationFailure failure = signInResult.UnwrapErr();
            SetServerError(failure.Message);
        }
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
        }
        finally
        {
            operationSource.Dispose();
        }
    }
}
