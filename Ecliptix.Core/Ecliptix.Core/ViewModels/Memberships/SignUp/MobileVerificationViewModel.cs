using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Membership;
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.ViewModels.Memberships.SignUp;

public class MobileVerificationViewModel : ViewModelBase, IActivatableViewModel, IRoutableViewModel
{
    private string _errorMessage = string.Empty;

    private string _mobileNumber = "+380970177443";

    private readonly ILocalizationService _localizationService;
    public ILocalizationService LocalizationService => _localizationService;

    public string? UrlPathSegment { get; } = "/mobile-verification";

    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, Unit> VerifyMobileNumberCommand { get; set; }

    public ViewModelActivator Activator { get; } = new();

    
    public string MobileNumber
    {
        get => _mobileNumber;
        set => this.RaiseAndSetIfChanged(ref _mobileNumber, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
    }

    public string InvalidFormatError =>
        _localizationService["Authentication.Registration.phoneVerification.error.invalidFormat"];

    public MobileVerificationViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen) : base(networkProvider)
    {
        _localizationService = localizationService;
        HostScreen = hostScreen;

        IObservable<bool> canExecute = this.WhenAnyValue(
            x => x.MobileNumber,
            number => string.IsNullOrWhiteSpace(MembershipValidation.Validate(ValidationType.PhoneNumber, number)));

        VerifyMobileNumberCommand = ReactiveCommand.CreateFromTask(ExecuteVerificationAsync, canExecute);
        
        this.WhenActivated(disposables =>
        {
            Observable.FromEvent(
                    handler => _localizationService.LanguageChanged += handler,
                    handler => _localizationService.LanguageChanged -= handler
                )
                .Subscribe(_ => { this.RaisePropertyChanged(nameof(InvalidFormatError)); })
                .DisposeWith(disposables);
        });
    }

    private async Task<Unit> ExecuteVerificationAsync()
    {
        string systemDeviceIdentifier = SystemDeviceIdentifier();

        ValidatePhoneNumberRequest request = CreateValidateRequest(systemDeviceIdentifier);
        uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

        Result<ShieldUnit, NetworkFailure> result = await NetworkProvider.ExecuteServiceRequestAsync(
            connectId,
            RcpServiceType.ValidatePhoneNumber,
            request.ToByteArray(),
            ServiceFlowType.Single,
            HandleValidationResponseAsync
        );

        if (result.IsOk)
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.VerificationCodeEntry);
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
            ErrorMessage = response.Message;
            return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value));
        }

        ErrorMessage = string.Empty;
        return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value));
    }
}