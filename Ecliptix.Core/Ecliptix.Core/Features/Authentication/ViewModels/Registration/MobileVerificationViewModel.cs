using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Utilities.Unit;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Serilog;

namespace Ecliptix.Core.Features.Authentication.ViewModels.Registration;

public class MobileVerificationViewModel : Core.MVVM.ViewModelBase, IRoutableViewModel, IResettable, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private bool _hasMobileNumberBeenTouched;
    private bool _isDisposed;
    private readonly IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    private readonly IRegistrationService _registrationService;
    
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
        IRegistrationService registrationService) : base(systemEventService, networkProvider, localizationService)
    {
        _registrationService = registrationService;
        HostScreen = hostScreen;
        _applicationSecureStorageProvider = applicationSecureStorageProvider;
        IObservable<bool> isFormLogicallyValid = SetupValidation();
        SetupCommands(isFormLogicallyValid);
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<string> mobileValidation = this.WhenAnyValue(x => x.MobileNumber)
            .Select(mobile => MobileNumberValidator.Validate(mobile, LocalizationService))
            .Replay(1)
            .RefCount();

        IObservable<string> mobileErrorStream = this.WhenAnyValue(x => x.MobileNumber, x => x.NetworkErrorMessage)
            .CombineLatest(mobileValidation, (inputs, validationError) =>
            {
                string mobile = inputs.Item1;
                string? networkError = inputs.Item2;
                
                if (!_hasMobileNumberBeenTouched && !string.IsNullOrWhiteSpace(mobile))
                    _hasMobileNumberBeenTouched = true;

                // Network errors take precedence over validation errors
                if (!string.IsNullOrEmpty(networkError))
                    return networkError;
                    
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
        NetworkErrorMessage = string.Empty;

        string systemDeviceIdentifier = SystemDeviceIdentifier();
        uint connectId = ComputeConnectId();
        
        Result<ByteString, string> result = 
            await _registrationService.ValidatePhoneNumberAsync(
                MobileNumber, 
                systemDeviceIdentifier,
                connectId);
        
        if (result.IsOk)
        {
            ByteString? mobileNumberIdentifier = result.Unwrap();
            
            if (mobileNumberIdentifier != null)
            {
                VerifyOtpViewModel vm = new(SystemEventService, NetworkProvider, LocalizationService, HostScreen, mobileNumberIdentifier, _applicationSecureStorageProvider);
                ((MembershipHostWindowModel)HostScreen).NavigateToViewModel(vm);
            }
            else
            {
                Log.Debug("Returned result is ok but mobile number identifier is null");
                Log.Debug("Result is "+ result.Unwrap());
            }
        }
        else
        {
            NetworkErrorMessage = result.UnwrapErr();
        }

        return Unit.Default;
    }

    private ValidatePhoneNumberRequest CreateValidateRequest(string systemDeviceIdentifier)
    {
        return new ValidatePhoneNumberRequest
        {
            MobileNumber = MobileNumber,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(systemDeviceIdentifier))
        };
    }
    
    public void ResetState()
    {
        MobileNumber = string.Empty;
        _hasMobileNumberBeenTouched = false;
        NetworkErrorMessage = string.Empty;
        HasMobileNumberError = false;
        MobileNumberError = string.Empty;
    }

    public new void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
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
