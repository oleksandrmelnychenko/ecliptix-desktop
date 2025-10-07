using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Utilities;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Features.Authentication.Common;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Unit = System.Reactive.Unit;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Authentication.Constants;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class MobileVerificationViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private bool _hasMobileNumberBeenTouched;
    private bool _isDisposed;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IOpaqueRegistrationService _registrationService;
    private readonly IUiDispatcher _uiDispatcher;

    [Reactive] public string? NetworkErrorMessage { get; private set; } = string.Empty;

    public string? UrlPathSegment { get; } = "/mobile-verification";

    public IScreen HostScreen { get; }

    [Reactive] public string MobileNumber { get; set; } = string.Empty;

    [ObservableAsProperty] public bool IsBusy { get; }

    [Reactive] public string? MobileNumberError { get; set; }
    [Reactive] public bool HasMobileNumberError { get; set; }

    public ReactiveCommand<Unit, Unit>? VerifyMobileNumberCommand { get; private set; }

    public MobileVerificationViewModel(
        ISystemEventService systemEventService,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider,
        IOpaqueRegistrationService registrationService,
        IUiDispatcher uiDispatcher) : base(systemEventService, networkProvider, localizationService)
    {
        _registrationService = registrationService;
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        _uiDispatcher = uiDispatcher;
        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<Unit> languageTrigger = LanguageChanged;

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
                    _hasMobileNumberBeenTouched = true;

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
        IObservable<bool> canVerify = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy)
            .CombineLatest(isFormLogicallyValid, (notBusy, isValid) => notBusy && isValid);

        VerifyMobileNumberCommand = ReactiveCommand.CreateFromTask(ExecuteVerificationAsync, canVerify);
        VerifyMobileNumberCommand.IsExecuting
            .ToPropertyEx(this, x => x.IsBusy)
            .DisposeWith(_disposables);

        _disposables.Add(VerifyMobileNumberCommand);
    }

    private async Task<Unit> ExecuteVerificationAsync()
    {
        if (_isDisposed) return Unit.Default;

        NetworkErrorMessage = string.Empty;

        using CancellationTokenSource timeoutCts = new(TimeSpan.FromSeconds(30));

        string systemDeviceIdentifier = SystemDeviceIdentifier();
        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

        Task<Result<ValidateMobileNumberResponse, string>> validationTask = _registrationService.ValidateMobileNumberAsync(
            MobileNumber,
            systemDeviceIdentifier,
            connectId);

        Result<ValidateMobileNumberResponse, string> result = await validationTask.WaitAsync(timeoutCts.Token);

        if (_isDisposed) return Unit.Default;

        if (result.IsOk)
        {
            ValidateMobileNumberResponse validateMobileNumberResponse = result.Unwrap();
            
            if (validateMobileNumberResponse.Membership != null)
                await HandleExistingMembershipAsync(validateMobileNumberResponse.Membership);
            else 
                await NavigateToOtpVerificationAsync(validateMobileNumberResponse.MobileNumberIdentifier);
        }
        else if (!_isDisposed)
        {
            NetworkErrorMessage = result.UnwrapErr();
            if (HostScreen is MembershipHostWindowModel hostWindow && !string.IsNullOrEmpty(NetworkErrorMessage))
                ShowServerErrorNotification(hostWindow, NetworkErrorMessage);
        }

        return Unit.Default;
    }

    private async Task HandleExistingMembershipAsync(Membership membership)
    {
        if (_isDisposed) return;
        
        if (HostScreen is not MembershipHostWindowModel hostWindow) return;

        switch (membership.CreationStatus)
        {
            case Protobuf.Membership.Membership.Types.CreationStatus.OtpVerified:
                await _applicationSecureStorageProvider.SetApplicationMembershipAsync(membership);
                await NavigateToSecureKeyConfirmationAsync();
                break;
            
            case Protobuf.Membership.Membership.Types.CreationStatus.SecureKeySet:
                await ShowAccountExistsRedirectAsync();
                break;
            
            default:
                NetworkErrorMessage = LocalizationService[AuthenticationConstants.UnexpectedMembershipStatusKey];
                ShowServerErrorNotification(hostWindow, NetworkErrorMessage);
                break;
        }
    }
    
    private Task NavigateToOtpVerificationAsync(ByteString mobileNumberIdentifier)
    {
        if (_isDisposed) return Task.CompletedTask;

        VerifyOtpViewModel vm = new(SystemEventService, NetworkProvider, LocalizationService, HostScreen,
            mobileNumberIdentifier, _applicationSecureStorageProvider, _registrationService, _uiDispatcher);

        if (!_isDisposed && HostScreen is MembershipHostWindowModel hostWindow)
        {
            hostWindow.RegistrationMobileNumber = MobileNumber;
            hostWindow.NavigateToViewModel(vm);
        }

        return Task.CompletedTask;
    }
    
    private Task NavigateToSecureKeyConfirmationAsync()
    {
        if (_isDisposed) return Task.CompletedTask;

        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            hostWindow.RegistrationMobileNumber = MobileNumber;
            hostWindow.Navigate.Execute(MembershipViewType.ConfirmSecureKey).Subscribe();
        }

        return Task.CompletedTask;
    }
    
    private Task ShowAccountExistsRedirectAsync()
    {
        if (_isDisposed) return Task.CompletedTask;

        string message = LocalizationService[AuthenticationConstants.AccountAlreadyExistsKey]; // "Account on this number already registered. Try sign in or use forgot password."

        if (HostScreen is MembershipHostWindowModel hostWindow)
        {
            ShowRedirectNotification(hostWindow, message, 8, () =>
            {
                if (!_isDisposed)
                {
                    CleanupAndNavigate(hostWindow, MembershipViewType.Welcome);
                }
            });
        }

        return Task.CompletedTask;
    }
    
    public async void HandleEnterKeyPress()
    {
        if (_isDisposed) return;

        if (VerifyMobileNumberCommand != null && await VerifyMobileNumberCommand.CanExecute.FirstOrDefaultAsync())
        {
            VerifyMobileNumberCommand.Execute().Subscribe().DisposeWith(_disposables);
        }
    }

    public void ResetState()
    {
        if (_isDisposed) return;

        MobileNumber = string.Empty;
        _hasMobileNumberBeenTouched = false;
        NetworkErrorMessage = string.Empty;
        HasMobileNumberError = false;
        MobileNumberError = string.Empty;
    }

    public new void Dispose()
    {
        Dispose(true);
    }

    protected override void Dispose(bool disposing)
    {
        if (_isDisposed) return;
        if (disposing)
        {
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}