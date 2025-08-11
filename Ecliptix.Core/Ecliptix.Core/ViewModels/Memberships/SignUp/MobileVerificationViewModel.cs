using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Core.Providers;
using Ecliptix.Core.Network.Services.Rpc;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Validations;
using Google.Protobuf;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Utilities.Unit;
using ValidationType = Ecliptix.Core.Services.Membership.ValidationType;

namespace Ecliptix.Core.ViewModels.Memberships.SignUp;

public class MobileVerificationViewModel : ViewModelBase, IRoutableViewModel, IDisposable
{
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<string> _mobileErrorSubject = new();
    private bool _hasMobileNumberBeenTouched;
    private bool _isDisposed;
    private IApplicationSecureStorageProvider _applicationSecureStorageProvider;
    
    public string? UrlPathSegment { get; } = "/mobile-verification";

    public IScreen HostScreen { get; }

    [Reactive] public string MobileNumber { get; set; } = string.Empty;

    private ByteString PhoneNumberIdentifier { get; set; }

    [ObservableAsProperty] public bool IsBusy { get; }

    [ObservableAsProperty] public string MobileNumberError { get; }

    [ObservableAsProperty] public bool HasMobileNumberError { get; }

    public ReactiveCommand<Unit, Unit> VerifyMobileNumberCommand { get; private set; }

    public MobileVerificationViewModel(
        ISystemEvents systemEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        IApplicationSecureStorageProvider applicationSecureStorageProvider) : base(systemEvents, networkProvider, localizationService)
    {
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

        IObservable<string> mobileErrorStream = this.WhenAnyValue(x => x.MobileNumber)
            .CombineLatest(mobileValidation, (mobile, error) =>
            {
                if (!_hasMobileNumberBeenTouched && !string.IsNullOrWhiteSpace(mobile))
                    _hasMobileNumberBeenTouched = true;

                return !_hasMobileNumberBeenTouched ? string.Empty : error;
            })
            .Replay(1)
            .RefCount();

        mobileErrorStream.Merge(_mobileErrorSubject)
            .ToPropertyEx(this, x => x.MobileNumberError);

        this.WhenAnyValue(x => x.MobileNumberError)
            .Select(e => !string.IsNullOrEmpty(e))
            .ToPropertyEx(this, x => x.HasMobileNumberError);

        return mobileValidation
            .Select(string.IsNullOrEmpty)
            .DistinctUntilChanged();
    }

    private void SetupCommands(IObservable<bool> isFormLogicallyValid)
    {
        IObservable<bool> canVerify = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy)
            .CombineLatest(isFormLogicallyValid, (notBusy, isValid) => notBusy && isValid);

        VerifyMobileNumberCommand = ReactiveCommand.CreateFromTask(ExecuteVerificationAsync, canVerify);
        VerifyMobileNumberCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);
    }

    private async Task<Unit> ExecuteVerificationAsync()
    {
        _mobileErrorSubject.OnNext(string.Empty);

        string systemDeviceIdentifier = SystemDeviceIdentifier();

        ValidatePhoneNumberRequest request = CreateValidateRequest(systemDeviceIdentifier);
        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<ShieldUnit, NetworkFailure> result = await NetworkProvider.ExecuteServiceRequestAsync(
            connectId,
            RpcServiceType.ValidatePhoneNumber,
            request.ToByteArray(),
            ServiceFlowType.Single,
            HandleValidationResponseAsync
        );

        if (result.IsOk)
        {
            
            VerifyOtpViewModel vm = new(SystemEvents, NetworkProvider, LocalizationService, HostScreen, PhoneNumberIdentifier, _applicationSecureStorageProvider);
            ((MembershipHostWindowModel)HostScreen).Router.Navigate.Execute(vm);
        }
        else
        {
            _mobileErrorSubject.OnNext(result.UnwrapErr().Message);
        }

        return Unit.Default;
    }

    private ValidatePhoneNumberRequest CreateValidateRequest(string systemDeviceIdentifier)
    {
        return new ValidatePhoneNumberRequest
        {
            PhoneNumber = MobileNumber,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(systemDeviceIdentifier))
        };
    }

    private Task<Result<ShieldUnit, NetworkFailure>> HandleValidationResponseAsync(byte[] payload)
    {
        ValidatePhoneNumberResponse response = Helpers.ParseFromBytes<ValidatePhoneNumberResponse>(payload);
        if (response.Result == VerificationResult.InvalidPhone)
        {
            _mobileErrorSubject.OnNext(response.Message);
            return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType(LocalizationService["ValidationErrors.Mobile.InvalidFormat"])));
        }

        PhoneNumberIdentifier = response.PhoneNumberIdentifier;

        _mobileErrorSubject.OnNext(string.Empty);
        return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value));
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
            _mobileErrorSubject.Dispose();
            _disposables.Dispose();
        }

        base.Dispose(disposing);
        _isDisposed = true;
    }
}
