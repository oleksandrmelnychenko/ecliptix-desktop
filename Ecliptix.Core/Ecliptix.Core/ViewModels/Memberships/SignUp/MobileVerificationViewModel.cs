using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
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
using Google.Protobuf;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using ShieldUnit = Ecliptix.Utilities.Unit;

namespace Ecliptix.Core.ViewModels.Memberships.SignUp;

public class MobileVerificationViewModel : ViewModelBase, IActivatableViewModel, IRoutableViewModel
{
    public ViewModelActivator Activator { get; } = new();

    private string _errorMessage = string.Empty;
    private string _mobileNumber = "+380970177443";

    private readonly ILocalizationService _localizationService;
    public ILocalizationService LocalizationService => _localizationService;

    public string? UrlPathSegment { get; } = "/mobile-verification";

    public IScreen HostScreen { get; }

    public ReactiveCommand<Unit, Unit> NavToVerifyOtp { get; set; }

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

        NavToVerifyOtp = ReactiveCommand.CreateFromTask(async () =>
        {
            string? systemDeviceIdentifier = SystemDeviceIdentifier();

            ValidatePhoneNumberRequest request = new()
            {
                PhoneNumber = MobileNumber,
                AppDeviceIdentifier = ByteString.CopyFrom(systemDeviceIdentifier, Encoding.UTF8),
            };

            uint connectId = ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect);

            _ = await NetworkProvider.ExecuteServiceRequestAsync(
                connectId,
                RcpServiceType.ValidatePhoneNumber,
                request.ToByteArray(),
                ServiceFlowType.Single,
                payload =>
                {
                    ValidatePhoneNumberResponse validatePhoneNumberResponse = Helpers.ParseFromBytes<ValidatePhoneNumberResponse>(payload);
                    if (validatePhoneNumberResponse.Result == VerificationResult.InvalidPhone)
                    {
                        ErrorMessage = validatePhoneNumberResponse.Message;
                    }
                    else
                    {
                        /*Task.Run(async () => await InitiateVerification(_validatePhoneNumberResponse.PhoneNumberIdentifier,
                            InitiateVerificationRequest.Types.Type.SendOtp), cancellationTokenSource.Token);*/
                    }

                    return Task.FromResult(Result<ShieldUnit, NetworkFailure>.Ok(ShieldUnit.Value));
                }
            );

            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.VerificationCodeEntry);
        });

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
}