using System;
using System.Buffers;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Persistors;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Authentication.ViewFactory;
using Ecliptix.Domain.Memberships;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protobuf.AppDevice;
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
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.ViewModels.Memberships.SignUp;

public class PasswordConfirmationViewModel : ViewModelBase, IRoutableViewModel
{
    private readonly SecureTextBuffer _passwordBuffer = new();
    private readonly SecureTextBuffer _verifyPasswordBuffer = new();
    private bool _hasPasswordBeenTouched;
    private bool _hasVerifyPasswordBeenTouched;
    
    
    public int CurrentPasswordLength => _passwordBuffer.Length;
    public int CurrentVerifyPasswordLength => _verifyPasswordBuffer.Length;

    [ObservableAsProperty] public string? PasswordError { get; private set; }
    [ObservableAsProperty] public bool HasPasswordError { get; private set; }
    [ObservableAsProperty] public string? VerifyPasswordError { get; private set; }
    [ObservableAsProperty] public bool HasVerifyPasswordError { get; private set; }
    
    [Reactive] public bool CanSubmit { get; private set; }
    [ObservableAsProperty] public bool IsBusy { get; }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SubmitCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> NavPassConfToPassPhase { get; }
    
    private ByteString VerificationSessionId { get; set; }

    private ISecureStorageProvider _secureStorageProvider;
    public PasswordConfirmationViewModel(
        ISystemEvents systemEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen,
        ISecureStorageProvider secureStorageProvider
    ) : base(systemEvents, networkProvider, localizationService)
    {
        HostScreen = hostScreen;
        _secureStorageProvider = secureStorageProvider;
        
        this.WhenActivated(disposables =>
        {
            this.WhenAnyValue(x => x.CanSubmit).BindTo(this, x => x.CanSubmit).DisposeWith(disposables);
            
            Observable.FromAsync(LoadMembershipAsync)
                .Subscribe()
                .DisposeWith(disposables);
        });
        
        IObservable<bool> isFormLogicallyValid = SetupValidation();

        IObservable<bool> canExecuteSubmit = this.WhenAnyValue(x => x.IsBusy, isBusy => !isBusy)
            .CombineLatest(isFormLogicallyValid, (notBusy, isValid) => notBusy && isValid);

        SubmitCommand = ReactiveCommand.CreateFromTask(SubmitRegistrationPasswordAsync);
        SubmitCommand.IsExecuting.ToPropertyEx(this, x => x.IsBusy);
        canExecuteSubmit.BindTo(this, x => x.CanSubmit);

        NavPassConfToPassPhase = ReactiveCommand.Create(() =>
        {
            ((MembershipHostWindowModel)HostScreen).Navigate.Execute(MembershipViewType.PassPhase);
        });
        
        
    }

    private async Task LoadMembershipAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> applicationInstance = await _secureStorageProvider.GetApplicationInstanceSettingsAsync();
        if (!applicationInstance.IsOk)
        {
            //TODO hadle error 
        }
        ApplicationInstanceSettings settings = applicationInstance.Unwrap();
        VerificationSessionId = settings.Membership.UniqueIdentifier;
    }

    public void InsertPasswordChars(int index, string chars)
    {
        if (!_hasPasswordBeenTouched) _hasPasswordBeenTouched = true;
        _passwordBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentPasswordLength));
    }

    public void RemovePasswordChars(int index, int count)
    {
        if (!_hasPasswordBeenTouched) _hasPasswordBeenTouched = true;
        _passwordBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentPasswordLength));
    }

    public void InsertVerifyPasswordChars(int index, string chars)
    {
        if (!_hasVerifyPasswordBeenTouched) _hasVerifyPasswordBeenTouched = true;
        _verifyPasswordBuffer.Insert(index, chars);
        this.RaisePropertyChanged(nameof(CurrentVerifyPasswordLength));
    }

    public void RemoveVerifyPasswordChars(int index, int count)
    {
        if (!_hasVerifyPasswordBeenTouched) _hasVerifyPasswordBeenTouched = true;
        _verifyPasswordBuffer.Remove(index, count);
        this.RaisePropertyChanged(nameof(CurrentVerifyPasswordLength));
    }

    private IObservable<bool> SetupValidation()
    {
        IObservable<(string? Error, string Recommendations)> passwordValidation = this.WhenAnyValue(x => x.CurrentPasswordLength)
            .Select(_ => ValidatePassword())
            .Replay(1)
            .RefCount();

        IObservable<string> passwordErrorStream = passwordValidation
            .Select(v => _hasPasswordBeenTouched ? FormatError(v.Error, v.Recommendations) : string.Empty)
            .Replay(1)
            .RefCount();

        passwordErrorStream.ToPropertyEx(this, x => x.PasswordError);
        this.WhenAnyValue(x => x.PasswordError).Select(e => !string.IsNullOrEmpty(e)).ToPropertyEx(this, x => x.HasPasswordError);

        IObservable<bool> isPasswordLogicallyValid = passwordValidation.Select(v => string.IsNullOrEmpty(v.Error));

        IObservable<bool> passwordsMatch = this.WhenAnyValue(x => x.CurrentPasswordLength, x => x.CurrentVerifyPasswordLength)
            .Select(_ => DoPasswordsMatch())
            .Replay(1)
            .RefCount();

        IObservable<string> verifyPasswordErrorStream = passwordsMatch
            .Select(match => _hasVerifyPasswordBeenTouched && !match ? LocalizationService["ValidationErrors.VerifySecureKey.DoesNotMatch"] : string.Empty)
            .Replay(1)
            .RefCount();

        verifyPasswordErrorStream.ToPropertyEx(this, x => x.VerifyPasswordError);
        this.WhenAnyValue(x => x.VerifyPasswordError).Select(e => !string.IsNullOrEmpty(e)).ToPropertyEx(this, x => x.HasVerifyPasswordError);

        return isPasswordLogicallyValid.CombineLatest(passwordsMatch, (isPassValid, areMatching) => isPassValid && areMatching)
            .DistinctUntilChanged();
    }

    private (string? Error, string Recommendations) ValidatePassword()
    {
        string? error = null;
        var recommendations = string.Empty;
        _passwordBuffer.WithSecureBytes(bytes =>
        {
            string password = Encoding.UTF8.GetString(bytes);
            (error, var recs) = SecureKeyValidator.Validate(password, LocalizationService);
            if (recs.Any())
            {
                recommendations = string.Join(Environment.NewLine, recs);
            }
        });
        return (error, recommendations);
    }
    
    private bool DoPasswordsMatch()
    {
        if (_passwordBuffer.Length != _verifyPasswordBuffer.Length)
        {
            return false;
        }

        if (_passwordBuffer.Length == 0)
        {
            return true;
        }

        byte[] passwordArray = new byte[_passwordBuffer.Length];
        byte[] verifyArray = new byte[_verifyPasswordBuffer.Length];

        _passwordBuffer.WithSecureBytes(passwordBytes =>
        {
            passwordBytes.CopyTo(passwordArray.AsSpan());
        });

        _verifyPasswordBuffer.WithSecureBytes(verifyBytes =>
        {
            verifyBytes.CopyTo(verifyArray.AsSpan());
        });

        return passwordArray.AsSpan().SequenceEqual(verifyArray);
    }

    private static string FormatError(string? error, string recommendations)
    {
        if (!string.IsNullOrEmpty(error)) return error;
        return recommendations;
    }
    
    

     private async Task SubmitRegistrationPasswordAsync()
    {
        if (IsBusy || !CanSubmit) return;

        byte[]? passwordBytes = null;
        try
        {
            _passwordBuffer.WithSecureBytes(bytes =>
            {
                passwordBytes = new byte[bytes.Length];
                bytes.CopyTo(passwordBytes);
            });

            Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> opfrResult =
                OpaqueProtocolService.CreateOprfRequest(passwordBytes);

            if (opfrResult.IsErr)
            {
                PasswordError = opfrResult.UnwrapErr().Message;
                return;
            }

            (byte[] OprfRequest, BigInteger Blind) opfr = opfrResult.Unwrap();

            OprfRegistrationInitRequest request = new()
            {
                MembershipIdentifier = VerificationSessionId,
                PeerOprf = ByteString.CopyFrom(opfr.OprfRequest)
            };

            await NetworkProvider.ExecuteServiceRequestAsync(
                ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
                RcpServiceType.OpaqueRegistrationInit,
                request.ToByteArray(),
                ServiceFlowType.Single,
                async payload =>
                {
                    OprfRegistrationInitResponse createMembershipResponse =
                        OprfRegistrationInitResponse.Parser.ParseFrom(payload);

                    Console.WriteLine("Received OPRF response");
                    return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                },
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            PasswordError = $"Submission failed: {ex.Message}";
        }
        finally
        {
            if (passwordBytes != null)
            {
                Array.Clear(passwordBytes, 0, passwordBytes.Length);
            }
        }
    }


    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _passwordBuffer.Dispose();
            _verifyPasswordBuffer.Dispose();
        }
        base.Dispose(disposing);
    }

    public string? UrlPathSegment { get; } = "/password-confirmation";
    public IScreen HostScreen { get; }
}