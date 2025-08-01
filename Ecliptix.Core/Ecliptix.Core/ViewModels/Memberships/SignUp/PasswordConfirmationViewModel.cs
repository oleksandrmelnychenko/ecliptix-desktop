using System;
using System.Buffers;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Domain.Memberships;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Org.BouncyCastle.Math;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships.SignUp;

public class PasswordConfirmationViewModel : ViewModelBase, IRoutableViewModel
{
    private PasswordManager? _passwordManager;
    private readonly SecureTextBuffer _passwordBuffer = new();
    private readonly SecureTextBuffer _verifyPasswordBuffer = new();
    private readonly IDisposable _mobileSubscription;

    public int CurrentPasswordLength => _passwordBuffer.Length;
    public int CurrentVerifyPasswordLength => _verifyPasswordBuffer.Length;

    private string _passwordErrorMessage = string.Empty;
    public string PasswordErrorMessage
    {
        get => _passwordErrorMessage;
        private set => this.RaiseAndSetIfChanged(ref _passwordErrorMessage, value);
    }

    private bool _isPasswordErrorVisible;
    public bool IsPasswordErrorVisible
    {
        get => _isPasswordErrorVisible;
        private set => this.RaiseAndSetIfChanged(ref _isPasswordErrorVisible, value);
    }

    private bool _canSubmit;
    public bool CanSubmit
    {
        get => _canSubmit;
        private set => this.RaiseAndSetIfChanged(ref _canSubmit, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SubmitCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavPassConfToPassPhase { get; }
    
    private string VerificationSessionId { get; set; }

    public PasswordConfirmationViewModel(
        ISystemEvents systemEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen
        ): base(systemEvents,networkProvider,localizationService)
    {
        HostScreen = hostScreen;
        
        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(
            x => x.CanSubmit,
            x => x.IsBusy,
            (cs, busy) => cs && !busy);

        NavPassConfToPassPhase = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.PassPhase);
        });

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitRegistrationPasswordAsync, canExecuteSubmit);

        _mobileSubscription = MessageBus.Current.Listen<string>("Mobile")
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(mobile => { VerificationSessionId = mobile; });
    }

    public void InsertPasswordChars(int index, string chars)
    {
        _passwordBuffer.Insert(index, chars);
        ValidatePasswordsFromBuffer();
        this.RaisePropertyChanged(nameof(CurrentPasswordLength));
    }

    public void RemovePasswordChars(int index, int count)
    {
        _passwordBuffer.Remove(index, count);
        ValidatePasswordsFromBuffer();
        this.RaisePropertyChanged(nameof(CurrentPasswordLength));
    }

    public void InsertVerifyPasswordChars(int index, string chars)
    {
        _verifyPasswordBuffer.Insert(index, chars);
        ValidatePasswordsFromBuffer();
        this.RaisePropertyChanged(nameof(CurrentVerifyPasswordLength));
    }

    public void RemoveVerifyPasswordChars(int index, int count)
    {
        _verifyPasswordBuffer.Remove(index, count);
        ValidatePasswordsFromBuffer();
        this.RaisePropertyChanged(nameof(CurrentVerifyPasswordLength));
    }

    private void ValidatePasswordsFromBuffer()
    {
        PasswordErrorMessage = string.Empty;
        IsPasswordErrorVisible = false;
        CanSubmit = false;

        if (_passwordBuffer.Length == 0)
        {
            if (_verifyPasswordBuffer.Length > 0)
            {
                PasswordErrorMessage = "Please enter your password in the first field.";
                IsPasswordErrorVisible = true;
            }
            return;
        }

        // Validate first password compliance
        var passwordString = string.Empty;
        var verifyPasswordString = string.Empty;
        
        _passwordBuffer.WithSecureBytes(passwordBytes =>
        {
            passwordString = Encoding.UTF8.GetString(passwordBytes);
        });

        _passwordManager ??= PasswordManager.Create().Unwrap();
        Result<Unit, EcliptixProtocolFailure> complianceResult =
            _passwordManager.CheckPasswordCompliance(passwordString, PasswordPolicy.Default);

        if (complianceResult.IsErr)
        {
            PasswordErrorMessage = complianceResult.UnwrapErr().Message;
            IsPasswordErrorVisible = true;
            return;
        }

        if (_verifyPasswordBuffer.Length == 0)
        {
            PasswordErrorMessage = "Please verify your password.";
            IsPasswordErrorVisible = true;
            return;
        }

        // Compare passwords
        _verifyPasswordBuffer.WithSecureBytes(verifyPasswordBytes =>
        {
            verifyPasswordString = Encoding.UTF8.GetString(verifyPasswordBytes);
        });

        if (passwordString != verifyPasswordString)
        {
            PasswordErrorMessage = "Passwords do not match.";
            IsPasswordErrorVisible = true;
            return;
        }

        IsPasswordErrorVisible = false;
        PasswordErrorMessage = string.Empty;
        CanSubmit = true;
    }

   private async Task SubmitRegistrationPasswordAsync()
{
    if (!CanSubmit || _passwordBuffer.Length == 0)
    {
        PasswordErrorMessage = "Submission requirements not met.";
        IsPasswordErrorVisible = true;
        return;
    }

    IsBusy = true;
    PasswordErrorMessage = string.Empty;
    IsPasswordErrorVisible = false;

    try
    {
        byte[] passwordBytes = null;
        
        // Extract password bytes from secure buffer
        _passwordBuffer.WithSecureBytes(bytes =>
        {
            passwordBytes = new byte[bytes.Length];
            bytes.CopyTo(passwordBytes);
        });

        string passwordString = Encoding.UTF8.GetString(passwordBytes);

        Result<string, EcliptixProtocolFailure> verifierResult = _passwordManager!.HashPassword(passwordString);
        if (verifierResult.IsErr)
        {
            throw new InvalidOperationException(
                $"Failed to hash password for server: {verifierResult.UnwrapErr().Message}");
        }

        Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> opfrResult =
            OpaqueProtocolService.CreateOprfRequest(passwordBytes);

        if (opfrResult.IsErr)
        {
            throw new InvalidOperationException(
                $"Failed to create OPRF request: {opfrResult.UnwrapErr().Message}");
        }

        (byte[] OprfRequest, BigInteger Blind) opfr = opfrResult.Unwrap();

        OprfRegistrationInitRequest request = new()
        {
            MembershipIdentifier = Helpers.GuidToByteString(Guid.Parse(VerificationSessionId)),
            PeerOprf = ByteString.CopyFrom(opfr.OprfRequest)
        };

        _ = await NetworkProvider.ExecuteServiceRequestAsync(
            ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
            RcpServiceType.OpaqueRegistrationInit,
            request.ToByteArray(),
            ServiceFlowType.Single,
            async payload =>
            {
                OprfRegistrationInitResponse createMembershipResponse =
                    Helpers.ParseFromBytes<OprfRegistrationInitResponse>(payload);

                if (createMembershipResponse.Result ==
                    OprfRegistrationInitResponse.Types.UpdateResult.Succeeded)
                {
                    Result<byte[], OpaqueFailure> envelope = OpaqueProtocolService.CreateRegistrationRecord(passwordBytes,
                        createMembershipResponse.PeerOprf.ToByteArray(), opfr.Blind);

                    OprfRegistrationCompleteRequest r = new()
                    {
                        MembershipIdentifier = createMembershipResponse.Membership.UniqueIdentifier,
                        PeerRegistrationRecord = ByteString.CopyFrom(envelope.Unwrap())
                    };

                    _ = await NetworkProvider.ExecuteServiceRequestAsync(
                        ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
                        RcpServiceType.OpaqueRegistrationComplete,
                        r.ToByteArray(),
                        ServiceFlowType.Single,
                        payload =>
                        {
                            OprfRegistrationCompleteResponse createMembershipResponse =
                                Helpers.ParseFromBytes<OprfRegistrationCompleteResponse>(payload);

                            // Registration completed successfully
                            return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                        });
                }

                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            },
            CancellationToken.None
        );

        // Clear sensitive data
        Array.Clear(passwordBytes, 0, passwordBytes.Length);
    }
    catch (Exception ex)
    {
        PasswordErrorMessage = $"Submission failed: {ex.Message}";
        IsPasswordErrorVisible = true;
    }
    finally
    {
        IsBusy = false;
    }
}
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _passwordBuffer?.Dispose();
            _verifyPasswordBuffer?.Dispose();
            _mobileSubscription?.Dispose();
        }
        base.Dispose(disposing);
    }

    public string? UrlPathSegment { get; } = "/password-confirmation";
    public IScreen HostScreen { get; }
}