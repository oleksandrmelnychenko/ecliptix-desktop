using System;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Settings.Constants;
using Ecliptix.Feature.Authentication.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.Authentication.ViewModels.Hosts;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Transport.Identity;
using Ecliptix.Utilities;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;
using AuthenticationFlowContext = Ecliptix.Core.Modularity.Abstractions.Authentication.AuthenticationFlowContext;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;
using Keys = Ecliptix.Core.Services.Authentication.Constants.AuthenticationConstants.MobileVerificationKeys;
using MembershipViewType = Ecliptix.Core.Modularity.Abstractions.Authentication.MembershipViewType;
using Unit = System.Reactive.Unit;

namespace Ecliptix.Feature.Authentication.Authentication.ViewModels.Registration;

public sealed partial class MobileVerificationViewModel : Core.Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IAuthRepository _authRepository;
    private readonly AuthenticationFlowContext _flowContext;
    private readonly IConnectivityService _connectivityService;
    private readonly CompositeDisposable _disposables = new();
    private readonly DefaultSystemSettings _settings;
    private readonly IMessageBus? _messageBus;

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

        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);

        SetupSubscriptions();
        AttemptAutoSwitchCountry();
    }

    public string? UrlPathSegment { get; } = "/mobile-verification";
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
        RawMobileNumber = string.Empty;
        _hasMobileNumberBeenTouched = false;
        HasMobileNumberError = false;
        MobileNumberError = string.Empty;
        _executionErrorSubject.OnNext(string.Empty);
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
            .WhenAnyValue(x => x.RawMobileNumber)
            .Select(_ => Unit.Default);

        IObservable<Unit> validationTrigger =
            mobileTrigger
                .Merge(languageTrigger);

        IObservable<string> mobileValidation = validationTrigger
            .Select(_ => MobileNumberValidator.Validate(RawMobileNumber, LocalizationService))
            .Replay(1)
            .RefCount();

        IObservable<string> mobileErrorStream = this.WhenAnyValue(x => x.RawMobileNumber)
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

    private void SetupSubscriptions()
    {
        LanguageChanged
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(_ => AttemptAutoSwitchCountry())
            .DisposeWith(_disposables);

        if (_messageBus != null)
        {
            _messageBus.Subscribe<CountryCodeSelectedEvent>(evt =>
                {
                    if (evt.RequestorContext != CountryPickerContext)
                    {
                        return Task.CompletedTask;
                    }

                    _hasManualCountrySelection = true;
                    CountryFlag = evt.SelectedCountry.FlagImagePath;
                    PhonePrefix = evt.SelectedCountry.PhonePrefix;
                    CountryIso = evt.SelectedCountry.IsoCode;
                    return Task.CompletedTask;
                })
                .DisposeWith(_disposables);
        }

    }

    private void AttemptAutoSwitchCountry()
    {
        if (_hasManualCountrySelection)
        {
            return;
        }

        if (!string.IsNullOrEmpty(RawMobileNumber))
        {
            return;
        }

        try
        {
            CultureInfo culture = LocalizationService.CurrentCultureInfo;

            (string Iso, string Prefix, string FlagPath)? countryData = AppCultureSettings.Default.ResolveCountryFromCulture(culture);

            if (countryData != null)
            {
                CountryIso = countryData.Value.Iso;
                PhonePrefix = countryData.Value.Prefix;
                CountryFlag = countryData.Value.FlagPath;
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Failed to auto-switch country context: " + ex.Message);
        }
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

        OpenCountryPickerCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await GlobalModalService.ShowRightAsync(
                new CountryCodeViewModel(_messageBus, CountryIso, CountryPickerContext),
                showScrim: true,
                isDismissable: true
            );
        });

        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() =>
        {
            bool success = BrowserHelper.OpenUrl(_settings.PrivacyPolicyUrl);
            if (!success)
            {
                Log.Warning("Failed to open privacy policy URL: {Url}", _settings.PrivacyPolicyUrl);
            }
        });

        OpenTermsOfServiceCommand = ReactiveCommand.Create(() =>
        {
            bool success = BrowserHelper.OpenUrl(_settings.TermsOfServiceUrl);
            if (!success)
            {
                Log.Warning("Failed to open privacy policy URL: {Url}", _settings.TermsOfServiceUrl);
            }
        });

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
            CancellationTokenSource cancellationTokenSource = RecreateCancellationToken(ref _cancellationTokenSource);

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);
            CancellationToken operationToken = cancellationTokenSource.Token;

            if (_flowContext == AuthenticationFlowContext.REGISTRATION)
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

        }
        catch (Exception ex)
        {
            string errorMessage = LocalizationService[AuthenticationConstants.COMMON_UNEXPECTED_ERROR_KEY];
            Log.Error(ex, "[MOBILE-VERIFICATION] Unexpected error during mobile verification");
            ShowError(errorMessage);
        }

        return Unit.Default;
    }

    private async Task ExecuteRegistrationFlowAsync(uint connectId, CancellationToken operationToken)
    {
        string fullNumber = PhoneNumberHelper.CombineWithPrefix(PhonePrefix, RawMobileNumber);

        Task<Result<ValidateMobileNumberResponse, string>> validationTask =
            _authRepository.ValidateMobileNumberAsync(fullNumber, connectId, operationToken);

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
            await _authRepository.CheckMobileNumberAvailabilityAsync(mobileNumberIdentifier, connectId,
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
                HasCreationStatus: true, CreationStatus: MembershipCreationStatus.MembershipOtpVerified
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
                : MembershipActivityStatus.MembershipActive,
            CreationStatus = statusResponse.CreationStatus
        };

        if (statusResponse.AccountUniqueIdentifier != null && !statusResponse.AccountUniqueIdentifier.IsEmpty)
        {
            membership.AccountUniqueIdentifier = statusResponse.AccountUniqueIdentifier;
        }

        await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership.UniqueIdentifier);

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
        string fullNumber = PhoneNumberHelper.CombineWithPrefix(PhonePrefix, RawMobileNumber);

        Task<Result<ByteString, string>> recoveryValidationTask =
            _authRepository.ValidateMobileForRecoveryAsync(fullNumber, connectId, operationToken);

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

        VerificationCodeEntryViewModel vm = new(
            _connectivityService,
            NetworkProvider,
            LocalizationService,
            HostScreen,
            (mobileNumberIdentifier, fullNumber),
            _applicationSecureStorageProvider,
            _authRepository,
            GlobalModalService,
            _flowContext);

        if (HostScreen is AuthenticationViewModel hostWindow)
        {
            hostWindow.RecoveryMobileNumber = fullNumber;
            hostWindow.NavigateToViewModel(vm);
        }
    }

    private void ShowError(string errorMessage)
    {
        if (!string.IsNullOrEmpty(errorMessage))
        {
            _executionErrorSubject.OnNext(errorMessage);
        }
    }

    private Task NavigateToOtpVerificationAsync(ByteString mobileNumberIdentifier)
    {
        if (_isDisposed)
        {
            return Task.CompletedTask;
        }
        string fullNumber = PhoneNumberHelper.CombineWithPrefix(PhonePrefix, RawMobileNumber);

        VerificationCodeEntryViewModel vm = new(
            _connectivityService,
            NetworkProvider,
            LocalizationService,
            HostScreen,
            (mobileNumberIdentifier, fullNumber),
            _applicationSecureStorageProvider,
            _authRepository,
            GlobalModalService);

        if (HostScreen is not AuthenticationViewModel hostWindow)
        {
            return Task.CompletedTask;
        }

        hostWindow.RegistrationMobileNumber = fullNumber;
        hostWindow.NavigateToViewModel(vm);

        return Task.CompletedTask;
    }

    private Task NavigateToSecureKeyAsync()
    {
        if (_isDisposed || HostScreen is not AuthenticationViewModel hostWindow)
        {
            return Task.CompletedTask;
        }

        hostWindow.RegistrationMobileNumber = RawMobileNumber;
        hostWindow.Navigate.Execute(MembershipViewType.SECURE_KEY_CONFIRMATION_VIEW).Subscribe();

        return Task.CompletedTask;
    }

    public new void Dispose() => Dispose(true);

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (disposing)
        {
            CancelCurrentOperation();
            _executionErrorSubject.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }

    private void CancelCurrentOperation()
    {
        CancellationTokenSource? operationSource = Interlocked.Exchange(
            ref _cancellationTokenSource,
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
