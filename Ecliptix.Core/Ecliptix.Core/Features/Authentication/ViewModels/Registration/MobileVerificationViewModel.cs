using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
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
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Keys = Ecliptix.Core.Services.Authentication.Constants.AuthenticationConstants.MobileVerificationKeys;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public sealed class MobileVerificationViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly ISecureKeyRecoveryService? _secureKeyRecoveryService;
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
        ISecureKeyRecoveryService secureKeyRecoveryService,
        AuthenticationFlowContext flowContext) : base(networkProvider, localizationService,
        connectivityService)
    {
        _registrationService = registrationService;
        _secureKeyRecoveryService = secureKeyRecoveryService;
        _connectivityService = connectivityService;
        _flowContext = flowContext;
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;

        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
    }

    public string? UrlPathSegment { get; } = "/mobile-verification";
    public IScreen HostScreen { get; }

    private string Localize(string registrationKey, string recoveryKey) =>
        GetSecureKeyLocalization(_flowContext, registrationKey, recoveryKey);

    public string Title => Localize(Keys.REGISTRATION_TITLE, Keys.RECOVERY_TITLE);

    public string Description => Localize(Keys.REGISTRATION_DESCRIPTION, Keys.RECOVERY_DESCRIPTION);

    public string Hint => Localize(Keys.REGISTRATION_HINT, Keys.RECOVERY_HINT);

    public string Watermark => Localize(Keys.REGISTRATION_WATERMARK, Keys.RECOVERY_WATERMARK);

    public string ButtonText => Localize(Keys.REGISTRATION_BUTTON, Keys.RECOVERY_BUTTON);

    public ReactiveCommand<Unit, Unit>? VerifyMobileNumberCommand { get; private set; }

    [Reactive] public string MobileNumber { get; set; } = string.Empty;
    [Reactive] public string? MobileNumberError { get; private set; }
    [Reactive] public bool HasMobileNumberError { get; private set; }

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
                await ExecuteRegistrationFlowAsync(connectId, operationToken);
            }
            else
            {
                await ExecuteRecoveryFlowAsync(connectId, operationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Operation was canceled.
        }
        catch (Exception)
        {
            if (!_isDisposed)
            {
                string errorMessage = LocalizationService[AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY];
                ShowError(errorMessage);
            }
        }

        return Unit.Default;
    }

    private async Task ExecuteRegistrationFlowAsync(uint connectId, CancellationToken operationToken)
    {
        Task<Result<ValidateMobileNumberResponse, string>> validationTask =
            _registrationService.ValidateMobileNumberAsync(MobileNumber, connectId, operationToken);

        Result<ValidateMobileNumberResponse, string> result = await validationTask;

        if (_isDisposed)
        {
            return;
        }

        if (result.IsErr)
        {
            ShowError(result.UnwrapErr());
            return;
        }

        ValidateMobileNumberResponse validateMobileNumberResponse = result.Unwrap();

        if (validateMobileNumberResponse.Result == VerificationResult.InvalidMobile)
        {
            ShowError(validateMobileNumberResponse.Message);
            return;
        }

        await HandleMobileAvailabilityCheckAsync(validateMobileNumberResponse.MobileNumberIdentifier, connectId,
            operationToken);
    }

    private async Task HandleMobileAvailabilityCheckAsync(ByteString mobileNumberIdentifier, uint connectId,
        CancellationToken operationToken)
    {
        Result<CheckMobileNumberAvailabilityResponse, string> statusResult =
            await _registrationService.CheckMobileNumberAvailabilityAsync(mobileNumberIdentifier, connectId,
                operationToken);

        if (_isDisposed)
        {
            return;
        }

        if (statusResult.IsErr)
        {
            ShowError(statusResult.UnwrapErr());
            return;
        }

        CheckMobileNumberAvailabilityResponse statusResponse = statusResult.Unwrap();
        await HandleAvailabilityStatusAsync(statusResponse, mobileNumberIdentifier);
    }

    private async Task HandleAvailabilityStatusAsync(CheckMobileNumberAvailabilityResponse statusResponse,
        ByteString mobileNumberIdentifier)
    {
        switch (statusResponse.Status)
        {
            case MobileAvailabilityStatus.Available:
                await NavigateToOtpVerificationAsync(mobileNumberIdentifier);
                break;

            case MobileAvailabilityStatus.RegistrationExpired:
                await NavigateToOtpVerificationAsync(mobileNumberIdentifier);
                break;

            case MobileAvailabilityStatus.IncompleteRegistration:
                await HandleIncompleteRegistrationAsync(statusResponse, mobileNumberIdentifier);
                break;

            case MobileAvailabilityStatus.DataCorruption:
                HandleDataCorruptionStatus(statusResponse);
                break;

            case MobileAvailabilityStatus.TakenActive:
            case MobileAvailabilityStatus.TakenInactive:
                HandleMobileTakenStatus(statusResponse);
                break;

            default:
                HandleUnexpectedStatus(statusResponse);
                break;
        }
    }

    private async Task HandleIncompleteRegistrationAsync(CheckMobileNumberAvailabilityResponse statusResponse,
        ByteString mobileNumberIdentifier)
    {
        if (statusResponse is
            {
                HasCreationStatus: true, CreationStatus: Protobuf.Membership.Membership.Types.CreationStatus.OtpVerified
            })
        {
            await StoreIncompleteMembershipAndNavigateAsync(statusResponse);
        }
        else
        {
            await NavigateToOtpVerificationAsync(mobileNumberIdentifier);
        }
    }

    private async Task StoreIncompleteMembershipAndNavigateAsync(CheckMobileNumberAvailabilityResponse statusResponse)
    {
        Membership membership = new()
        {
            UniqueIdentifier = statusResponse.ExistingMembershipId,
            Status = statusResponse.HasActivityStatus
                ? statusResponse.ActivityStatus
                : Protobuf.Membership.Membership.Types.ActivityStatus.Active,
            CreationStatus = statusResponse.CreationStatus
        };

        if (statusResponse.AccountUniqueIdentifier != null && !statusResponse.AccountUniqueIdentifier.IsEmpty)
        {
            membership.AccountUniqueIdentifier = statusResponse.AccountUniqueIdentifier;
        }

        await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);

        if (statusResponse.AccountUniqueIdentifier != null && !statusResponse.AccountUniqueIdentifier.IsEmpty)
        {
            await _applicationSecureStorageProvider.SetCurrentAccountIdAsync(statusResponse.AccountUniqueIdentifier)
                .ConfigureAwait(false);
        }

        await NavigateToSecureKeyAsync();
    }

    private void HandleDataCorruptionStatus(CheckMobileNumberAvailabilityResponse statusResponse)
    {
        string corruptionError = !string.IsNullOrEmpty(statusResponse.LocalizationKey)
            ? LocalizationService[statusResponse.LocalizationKey]
            : LocalizationService["MobileVerification.ERROR.DataCorruption"];
        ShowError(corruptionError);
    }

    private void HandleMobileTakenStatus(CheckMobileNumberAvailabilityResponse statusResponse)
    {
        string takenError = !string.IsNullOrEmpty(statusResponse.LocalizationKey)
            ? LocalizationService[statusResponse.LocalizationKey]
            : LocalizationService["MobileVerification.ERROR.MobileAlreadyRegistered"];
        ShowError(takenError);
    }

    private void HandleUnexpectedStatus(CheckMobileNumberAvailabilityResponse statusResponse)
    {
        string defaultError = !string.IsNullOrEmpty(statusResponse.LocalizationKey)
            ? LocalizationService[statusResponse.LocalizationKey]
            : LocalizationService["MobileVerification.ERROR.MobileAlreadyRegistered"];
        ShowError(defaultError);
    }

    private async Task ExecuteRecoveryFlowAsync(uint connectId, CancellationToken operationToken)
    {
        Task<Result<ByteString, string>> recoveryValidationTask =
            _secureKeyRecoveryService!.ValidateMobileForRecoveryAsync(MobileNumber, connectId, operationToken);

        Result<ByteString, string> result = await recoveryValidationTask;

        if (_isDisposed)
        {
            return;
        }

        if (result.IsErr)
        {
            ShowError(result.UnwrapErr());
            return;
        }

        ByteString mobileNumberIdentifier = result.Unwrap();

        VerifyOtpViewModel vm = new(_connectivityService, NetworkProvider, LocalizationService, HostScreen,
            mobileNumberIdentifier, _applicationSecureStorageProvider, _registrationService,
            _flowContext, _secureKeyRecoveryService);

        if (HostScreen is AuthenticationViewModel hostWindow)
        {
            hostWindow.RecoveryMobileNumber = MobileNumber;
            hostWindow.NavigateToViewModel(vm);
        }
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

        if (HostScreen is AuthenticationViewModel hostWindow)
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

        if (HostScreen is AuthenticationViewModel hostWindow)
        {
            hostWindow.RegistrationMobileNumber = MobileNumber;
            hostWindow.Navigate.Execute(MembershipViewType.SecureKeyConfirmationView).Subscribe();
        }

        return Task.CompletedTask;
    }

    public new void Dispose()
    {
        Dispose(true);
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
                // Intentionally suppressed: CancellationTokenSource already disposed during cleanup
            }
            finally
            {
                operationSource.Dispose();
            }
        }
    }
}
