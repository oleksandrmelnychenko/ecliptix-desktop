using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;

using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Messaging.Connectivity;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Authentication.Common;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;

using Google.Protobuf;

using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public sealed class MobileVerificationViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly IAuthenticationService _authenticationService;
    private readonly IPasswordRecoveryService? _passwordRecoveryService;
    private readonly AuthenticationFlowContext _flowContext;
    private readonly IConnectivityService _connectivityService;
    private readonly CompositeDisposable _disposables = new();

    private CancellationTokenSource? _currentOperationCts;
    private bool _hasMobileNumberBeenTouched;
    private bool _isDisposed;

    public MobileVerificationViewModel(
        IConnectivityService connectivityService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        IAuthenticationService authenticationService,
        IPasswordRecoveryService passwordRecoveryService,
        AuthenticationFlowContext flowContext) : base(networkProvider, localizationService,
        connectivityService)
    {
        _registrationService = registrationService;
        _authenticationService = authenticationService;
        _passwordRecoveryService = passwordRecoveryService;
        _connectivityService = connectivityService;
        _flowContext = flowContext;
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;

        Log.Information("[MOBILE-VERIFICATION-VM] Initialized with flow context: {FlowContext}", flowContext);

        if (_flowContext == AuthenticationFlowContext.PasswordRecovery && passwordRecoveryService == null)
        {
            throw new ArgumentNullException(nameof(passwordRecoveryService),
                "Password recovery service is required when flow context is PasswordRecovery");
        }

        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
    }

    public string? UrlPathSegment { get; } = "/mobile-verification";
    public IScreen HostScreen { get; }

    public string Title => GetSecureKeyLocalization(
        _flowContext,
        AuthenticationConstants.MobileVerificationKeys.RegistrationTitle,
        AuthenticationConstants.MobileVerificationKeys.RecoveryTitle);

    public string Description => GetSecureKeyLocalization(
        _flowContext,
        AuthenticationConstants.MobileVerificationKeys.RegistrationDescription,
        AuthenticationConstants.MobileVerificationKeys.RecoveryDescription);

    public string Hint => GetSecureKeyLocalization(
        _flowContext,
        AuthenticationConstants.MobileVerificationKeys.RegistrationHint,
        AuthenticationConstants.MobileVerificationKeys.RecoveryHint);

    public string Watermark => GetSecureKeyLocalization(
        _flowContext,
        AuthenticationConstants.MobileVerificationKeys.RegistrationWatermark,
        AuthenticationConstants.MobileVerificationKeys.RecoveryWatermark);

    public string ButtonText => GetSecureKeyLocalization(
        _flowContext,
        AuthenticationConstants.MobileVerificationKeys.RegistrationButton,
        AuthenticationConstants.MobileVerificationKeys.RecoveryButton);

    public ReactiveCommand<Unit, Unit>? VerifyMobileNumberCommand { get; private set; }

    [Reactive] public string MobileNumber { get; set; } = string.Empty;
    [Reactive] public string? MobileNumberError { get; set; }
    [Reactive] public bool HasMobileNumberError { get; set; }

    [ObservableAsProperty] public bool IsBusy { get; }

    public async Task HandleEnterKeyPressAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        if (VerifyMobileNumberCommand != null && await VerifyMobileNumberCommand.CanExecute.FirstOrDefaultAsync())
        {
            VerifyMobileNumberCommand.Execute().Subscribe().DisposeWith(_disposables);
        }
    }

    public void ResetState()
    {
        if (_isDisposed)
        {
            return;
        }

        CancelCurrentOperation();
        MobileNumber = string.Empty;
        _hasMobileNumberBeenTouched = false;
        HasMobileNumberError = false;
        MobileNumberError = string.Empty;
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<Unit> languageTrigger = LanguageChanged;

        languageTrigger
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(Title));
                this.RaisePropertyChanged(nameof(Description));
                this.RaisePropertyChanged(nameof(Hint));
                this.RaisePropertyChanged(nameof(Watermark));
                this.RaisePropertyChanged(nameof(ButtonText));
            })
            .DisposeWith(_disposables);

        IObservable<Unit> mobileTrigger = this
            .WhenAnyValue(x => x.MobileNumber)
            .Select(_ => Unit.Default);

        IObservable<Unit> validationTrigger =
            mobileTrigger
                .Merge(languageTrigger);

        IObservable<string> mobileValidation = validationTrigger
            .Select(_ => MobileNumberValidator.Validate(MobileNumber, LocalizationService))
            .Replay(1)
            .RefCount();

        IObservable<string> mobileErrorStream = this.WhenAnyValue(x => x.MobileNumber)
            .CombineLatest(mobileValidation, (mobile, validationError) =>
            {
                if (!_hasMobileNumberBeenTouched && !string.IsNullOrWhiteSpace(mobile))
                {
                    _hasMobileNumberBeenTouched = true;
                }

                return !_hasMobileNumberBeenTouched ? string.Empty : validationError;
            })
            .Replay(1)
            .RefCount();

        mobileErrorStream
            .Subscribe(error =>
            {
                MobileNumberError = error;
                HasMobileNumberError = !string.IsNullOrEmpty(error);
            })
            .DisposeWith(_disposables);

        return mobileValidation
            .Select(string.IsNullOrEmpty)
            .DistinctUntilChanged();
    }

    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> canVerify = this.WhenAnyValue(x => x.IsBusy, x => x.IsInNetworkOutage,
                (isBusy, isInOutage) => !isBusy && !isInOutage)
            .CombineLatest(isFormLogicallyValid, (canExecute, isValid) => canExecute && isValid);

        VerifyMobileNumberCommand = ReactiveCommand.CreateFromTask(ExecuteVerificationAsync, canVerify);
        VerifyMobileNumberCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        _disposables.Add(VerifyMobileNumberCommand);
    }

    private async Task<Unit> ExecuteVerificationAsync()
    {
        if (_isDisposed)
        {
            return Unit.Default;
        }

        try
        {
            CancellationTokenSource cancellationTokenSource = RecreateCancellationToken(ref _currentOperationCts);

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
            CancellationToken operationToken = cancellationTokenSource.Token;

            if (_flowContext == AuthenticationFlowContext.Registration)
            {
                Task<Result<ValidateMobileNumberResponse, string>> validationTask =
                    _registrationService.ValidateMobileNumberAsync(
                        MobileNumber,
                        connectId,
                        operationToken);

                Result<ValidateMobileNumberResponse, string> result = await validationTask;

                if (_isDisposed)
                {
                    return Unit.Default;
                }

                if (result.IsOk)
                {
                    ValidateMobileNumberResponse validateMobileNumberResponse = result.Unwrap();

                    if (validateMobileNumberResponse.Result == VerificationResult.InvalidMobile)
                    {
                        ShowError(validateMobileNumberResponse.Message);
                        return Unit.Default;
                    }

                    Result<CheckMobileNumberAvailabilityResponse, string> statusResult =
                        await _registrationService.CheckMobileNumberAvailabilityAsync(
                            validateMobileNumberResponse.MobileNumberIdentifier,
                            connectId,
                            operationToken);

                    if (_isDisposed)
                    {
                        return Unit.Default;
                    }

                    if (statusResult.IsOk)
                    {
                        CheckMobileNumberAvailabilityResponse statusResponse = statusResult.Unwrap();

                        switch (statusResponse.Status)
                        {
                            case MobileAvailabilityStatus.Available:
                                Serilog.Log.Information("[MOBILE-VERIFICATION] Mobile available, starting OTP verification");
                                await NavigateToOtpVerificationAsync(validateMobileNumberResponse.MobileNumberIdentifier);
                                break;

                            case MobileAvailabilityStatus.RegistrationExpired:
                                Serilog.Log.Information(
                                    "[MOBILE-VERIFICATION] Registration window expired (1 hour), starting fresh OTP verification");
                                await NavigateToOtpVerificationAsync(validateMobileNumberResponse.MobileNumberIdentifier);
                                break;

                            case MobileAvailabilityStatus.IncompleteRegistration:
                                string membershipIdStr = statusResponse.ExistingMembershipId != null &&
                                                         !statusResponse.ExistingMembershipId.IsEmpty
                                    ? Helpers.FromByteStringToGuid(statusResponse.ExistingMembershipId).ToString()
                                    : "Unknown";

                                if (statusResponse.HasCreationStatus &&
                                    statusResponse.CreationStatus ==
                                    Protobuf.Membership.Membership.Types.CreationStatus.OtpVerified)
                                {
                                    Protobuf.Membership.Membership membership = new Protobuf.Membership.Membership
                                    {
                                        UniqueIdentifier = statusResponse.ExistingMembershipId,
                                        Status = statusResponse.HasActivityStatus
                                            ? statusResponse.ActivityStatus
                                            : Protobuf.Membership.Membership.Types.ActivityStatus.Active,
                                        CreationStatus = statusResponse.CreationStatus
                                    };

                                    if (statusResponse.AccountUniqueIdentifier != null &&
                                        !statusResponse.AccountUniqueIdentifier.IsEmpty)
                                    {
                                        membership.AccountUniqueIdentifier = statusResponse.AccountUniqueIdentifier;
                                    }

                                    await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);

                                    if (statusResponse.AccountUniqueIdentifier != null &&
                                        !statusResponse.AccountUniqueIdentifier.IsEmpty)
                                    {
                                        await _applicationSecureStorageProvider
                                            .SetCurrentAccountIdAsync(statusResponse.AccountUniqueIdentifier)
                                            .ConfigureAwait(false);
                                        Serilog.Log.Information(
                                            "[MOBILE-VERIFICATION] Stored incomplete registration membership. MembershipId: {MembershipId}, AccountId: {AccountId}",
                                            membershipIdStr,
                                            Helpers.FromByteStringToGuid(statusResponse.AccountUniqueIdentifier));
                                    }
                                    else
                                    {
                                        Serilog.Log.Information(
                                            "[MOBILE-VERIFICATION] Stored incomplete registration membership. MembershipId: {MembershipId}",
                                            membershipIdStr);
                                    }

                                    await NavigateToSecureKeyAsync();
                                }
                                else
                                {
                                    Serilog.Log.Information(
                                        "[MOBILE-VERIFICATION] Incomplete registration - navigating to OTP. MembershipId: {MembershipId}, CreationStatus: {CreationStatus}",
                                        membershipIdStr,
                                        statusResponse.HasCreationStatus
                                            ? statusResponse.CreationStatus.ToString()
                                            : "Not Set");
                                    await NavigateToOtpVerificationAsync(validateMobileNumberResponse
                                        .MobileNumberIdentifier);
                                }

                                break;

                            case MobileAvailabilityStatus.DataCorruption:
                                string corruptedMembershipId = statusResponse.ExistingMembershipId != null &&
                                                               !statusResponse.ExistingMembershipId.IsEmpty
                                    ? Helpers.FromByteStringToGuid(statusResponse.ExistingMembershipId).ToString()
                                    : "Unknown";

                                Serilog.Log.Warning(
                                    "[MOBILE-VERIFICATION] Data corruption detected. MembershipId: {MembershipId}",
                                    corruptedMembershipId);

                                string corruptionError = !string.IsNullOrEmpty(statusResponse.LocalizationKey)
                                    ? LocalizationService[statusResponse.LocalizationKey]
                                    : LocalizationService["MobileVerification.Error.DataCorruption"];
                                ShowError(corruptionError);
                                break;

                            case MobileAvailabilityStatus.TakenActive:
                            case MobileAvailabilityStatus.TakenInactive:
                                if (statusResponse.RegisteredDeviceId != null &&
                                    !statusResponse.RegisteredDeviceId.IsEmpty)
                                {
                                    Guid registeredDevice = Helpers.FromByteStringToGuid(statusResponse.RegisteredDeviceId);
                                    Serilog.Log.Information(
                                        "[MOBILE-VERIFICATION] Mobile taken on device: {DeviceId}, Status: {Status}",
                                        registeredDevice, statusResponse.Status);
                                }

                                string takenError = !string.IsNullOrEmpty(statusResponse.LocalizationKey)
                                    ? LocalizationService[statusResponse.LocalizationKey]
                                    : LocalizationService["MobileVerification.Error.MobileAlreadyRegistered"];
                                ShowError(takenError);
                                break;

                            default:
                                Serilog.Log.Warning(
                                    "[MOBILE-VERIFICATION] Unexpected status: {Status}", statusResponse.Status);
                                string defaultError = !string.IsNullOrEmpty(statusResponse.LocalizationKey)
                                    ? LocalizationService[statusResponse.LocalizationKey]
                                    : LocalizationService["MobileVerification.Error.MobileAlreadyRegistered"];
                                ShowError(defaultError);
                                break;
                        }
                    }
                    else
                    {
                        ShowError(statusResult.UnwrapErr());
                    }
                }
                else if (!_isDisposed)
                {
                    ShowError(result.UnwrapErr());
                }
            }
            else
            {
                Task<Result<ByteString, string>> recoveryValidationTask =
                    _passwordRecoveryService!.ValidateMobileForRecoveryAsync(MobileNumber,
                        connectId, operationToken);

                Result<ByteString, string> result = await recoveryValidationTask;

                if (_isDisposed)
                {
                    return Unit.Default;
                }

                if (result.IsOk)
                {
                    ByteString mobileNumberIdentifier = result.Unwrap();

                    VerifyOtpViewModel vm = new(_connectivityService, NetworkProvider, LocalizationService, HostScreen,
                        mobileNumberIdentifier, _applicationSecureStorageProvider, _registrationService,
                        _flowContext, _passwordRecoveryService);

                    if (!_isDisposed && HostScreen is AuthenticationViewModel hostWindow)
                    {
                        hostWindow.RecoveryMobileNumber = MobileNumber;
                        hostWindow.NavigateToViewModel(vm);
                    }
                }
                else if (!_isDisposed)
                {
                    ShowError(result.UnwrapErr());
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            if (!_isDisposed)
            {
                string errorMessage = LocalizationService[AuthenticationConstants.CommonUnexpectedErrorKey];
                ShowError(errorMessage);
            }
        }

        return Unit.Default;
    }

    private void ShowError(string errorMessage)
    {
        if (HostScreen is AuthenticationViewModel hostWindow &&
            !string.IsNullOrEmpty(errorMessage))
        {
            ShowServerErrorNotification(hostWindow, errorMessage);
        }
    }

    private Task NavigateToOtpVerificationAsync(ByteString mobileNumberIdentifier)
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }

        VerifyOtpViewModel vm = new(_connectivityService, NetworkProvider, LocalizationService, HostScreen,
            mobileNumberIdentifier, _applicationSecureStorageProvider, _registrationService);

        if (!_isDisposed && HostScreen is AuthenticationViewModel hostWindow)
        {
            hostWindow.RegistrationMobileNumber = MobileNumber;
            hostWindow.NavigateToViewModel(vm);
        }

        return Task.CompletedTask;
    }

    private Task NavigateToSecureKeyAsync()
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }

        if (!_isDisposed && HostScreen is AuthenticationViewModel hostWindow)
        {
            hostWindow.RegistrationMobileNumber = MobileNumber;
            hostWindow.Navigate.Execute(MembershipViewType.ConfirmSecureKey).Subscribe();
        }

        return Task.CompletedTask;
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }

    private void CancelCurrentOperation()
    {
        CancellationTokenSource? operationSource = Interlocked.Exchange(
            ref _currentOperationCts,
            null);

        if (operationSource != null)
        {
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
}
