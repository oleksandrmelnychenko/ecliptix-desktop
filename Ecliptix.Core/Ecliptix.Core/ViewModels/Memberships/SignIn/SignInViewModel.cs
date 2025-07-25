using System;
using System.Buffers;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Ecliptix.Utilities.Membership;
using Google.Protobuf;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using ReactiveUI;

namespace Ecliptix.Core.ViewModels.Memberships.SignIn;

public sealed class SignInViewModel : ViewModelBase, IRoutableViewModel, IDisposable
{
    private string _mobileNumber = string.Empty;
    private string _passwordErrorMessage = string.Empty;
    private int _currentPasswordLength;
    private bool _isErrorVisible;
    private bool _isBusy;
    private SodiumSecureMemoryHandle? _securePasswordHandle;
    private bool _isDisposed;

    public string UrlPathSegment => "/sign-in";
    public IScreen HostScreen { get; }

    public int CurrentPasswordLength
    {
        get => _currentPasswordLength;
        private set => this.RaiseAndSetIfChanged(ref _currentPasswordLength, value);
    }
    
    public string MobileNumber
    {
        get => _mobileNumber;
        set => this.RaiseAndSetIfChanged(ref _mobileNumber, value);
    }

    public string PasswordErrorMessage
    {
        get => _passwordErrorMessage;
        private set => this.RaiseAndSetIfChanged(ref _passwordErrorMessage, value);
    }

    public bool IsErrorVisible
    {
        get => _isErrorVisible;
        private set => this.RaiseAndSetIfChanged(ref _isErrorVisible, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => this.RaiseAndSetIfChanged(ref _isBusy, value);
    }

    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SignInCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> AccountRecoveryCommand { get; }

    public SignInViewModel(
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen) : base(networkProvider, localizationService)
    {
        HostScreen = hostScreen;

        IObservable<bool> canExecute = this.WhenAnyValue(
                x => x.MobileNumber,
                x => x.PasswordErrorMessage,
                x => x.CurrentPasswordLength,
                (number, error, passwordLength) =>
                    string.IsNullOrWhiteSpace(MembershipValidation.Validate(ValidationType.MobileNumber, number)) &&
                    passwordLength >= 8 &&
                    string.IsNullOrEmpty(error))
            .Throttle(TimeSpan.FromMilliseconds(20))
            .DistinctUntilChanged()
            .ObserveOn(RxApp.MainThreadScheduler);

        SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync, canExecute);
        AccountRecoveryCommand = ReactiveCommand.Create(() =>
        {
            
        });
    }

    public void InsertPasswordChars(int index, string chars)
    {
        if (string.IsNullOrEmpty(chars)) return;
        ClearError();
        ModifySecurePassword(index, 0, chars);
    }

    public void RemovePasswordChars(int index, int count)
    {
        if (count <= 0) return;
        ClearError();
        ModifySecurePassword(index, count, string.Empty);
    }

    private void ModifySecurePassword(int index, int removeCount, string insertChars)
    {
        byte[]? oldPasswordBytes = null;
        byte[]? newPasswordBytes = null;
        SodiumSecureMemoryHandle? newHandle = null;

        try
        {
            int oldLength = _securePasswordHandle?.Length ?? 0;
            if (oldLength > 0)
            {
                oldPasswordBytes = ArrayPool<byte>.Shared.Rent(oldLength);
                Result<Unit, SodiumFailure> readResult = _securePasswordHandle!.Read(oldPasswordBytes.AsSpan(0, oldLength));
                if (readResult.IsErr)
                {
                    SetError($"System Error: {readResult.UnwrapErr().Message}");
                    return;
                }
            }
            ReadOnlySpan<byte> oldSpan = oldLength > 0 ? oldPasswordBytes.AsSpan(0, oldLength) : ReadOnlySpan<byte>.Empty;

            byte[] insertBytes = Encoding.UTF8.GetBytes(insertChars);

            if (index > oldSpan.Length) index = oldSpan.Length;
            removeCount = Math.Min(removeCount, oldSpan.Length - index);

            int newLength = oldSpan.Length - removeCount + insertBytes.Length;
            if (newLength > 0)
            {
                newPasswordBytes = ArrayPool<byte>.Shared.Rent(newLength);
                Span<byte> newSpan = newPasswordBytes.AsSpan(0, newLength);

                oldSpan[..index].CopyTo(newSpan);
                insertBytes.CopyTo(newSpan[index..]);
                if (index + removeCount < oldSpan.Length)
                {
                    oldSpan[(index + removeCount)..].CopyTo(newSpan[(index + insertBytes.Length)..]);
                }
            }
            
            Result<SodiumSecureMemoryHandle, SodiumFailure> allocateResult = SodiumSecureMemoryHandle.Allocate(newLength);
            if (allocateResult.IsErr)
            {
                SetError($"System Error: {allocateResult.UnwrapErr().Message}");
                return;
            }
            newHandle = allocateResult.Unwrap();

            if (newLength > 0)
            {
                Result<Unit, SodiumFailure> writeResult = newHandle.Write(newPasswordBytes.AsSpan(0, newLength));
                if (writeResult.IsErr)
                {
                    SetError($"System Error: {writeResult.UnwrapErr().Message}");
                    newHandle.Dispose();
                    return;
                }
            }
            
            SodiumSecureMemoryHandle? oldHandle = _securePasswordHandle;
            _securePasswordHandle = newHandle;
            oldHandle?.Dispose();
            newHandle = null; 

            CurrentPasswordLength = _securePasswordHandle.Length;
        }
        finally
        {
            if (oldPasswordBytes != null) ArrayPool<byte>.Shared.Return(oldPasswordBytes, true);
            if (newPasswordBytes != null) ArrayPool<byte>.Shared.Return(newPasswordBytes, true);
            newHandle?.Dispose();
        }
    }
    
    private async Task SignInAsync()
    {
        IsBusy = true;
        ClearError();

        if (_securePasswordHandle == null || _securePasswordHandle.IsInvalid)
        {
            SetError("Password is required.");
            IsBusy = false;
            return;
        }

        byte[]? rentedPasswordBytes = null;
        try
        {
            rentedPasswordBytes = ReadPassword();
            if (rentedPasswordBytes == null) return; 

            OpaqueProtocolService clientOpaqueService = CreateOpaqueService();
            (byte[] OprfRequest, BigInteger Blind)? requestTuple = CreateOprfRequest(rentedPasswordBytes);
            if (requestTuple == null) return;

            byte[] oprfRequest = requestTuple.Value.OprfRequest;
            BigInteger blind = requestTuple.Value.Blind;

            byte[] passwordBytes = rentedPasswordBytes.AsSpan().ToArray();

            Result<Unit, NetworkFailure> initResult = await SendInitRequestAndProcessResponse(clientOpaqueService, oprfRequest, blind, passwordBytes);
            if (initResult.IsErr)
            {
                SetError($"Sign-in init failed: {initResult.UnwrapErr().Message}");
            }
        }
        catch (Exception ex)
        {
            SetError($"An unexpected error occurred: {ex.Message}");
        }
        finally
        {
            if (rentedPasswordBytes != null)
            {
                rentedPasswordBytes.AsSpan().Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }
            IsBusy = false;
        }
    }

    private byte[]? ReadPassword()
    {
        byte[]? rentedPasswordBytes = null;
        try
        {
            int passwordLength = _securePasswordHandle!.Length;
            rentedPasswordBytes = ArrayPool<byte>.Shared.Rent(passwordLength);
            Span<byte> passwordSpan = rentedPasswordBytes.AsSpan(0, passwordLength);

            Result<Unit, SodiumFailure> readResult = _securePasswordHandle.Read(passwordSpan);
            if (readResult.IsErr)
            {
                SetError($"System error: Failed to read password securely. {readResult.UnwrapErr().Message}");
                return null;
            }

            return rentedPasswordBytes;
        }
        catch
        {
            if (rentedPasswordBytes != null)
            {
                rentedPasswordBytes.AsSpan().Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }
            throw;
        }
    }

    private OpaqueProtocolService CreateOpaqueService()
    {
        byte[] serverPublicKeyBytes = ServerPublicKey(); 
        ECPublicKeyParameters serverStaticPublicKeyParam = new(
            OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(serverPublicKeyBytes),
            OpaqueCryptoUtilities.DomainParams
        );
        return new OpaqueProtocolService(serverStaticPublicKeyParam);
    }

    private (byte[] OprfRequest, BigInteger Blind)? CreateOprfRequest(byte[] passwordBytes)
    {
        Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> oprfResult =
            OpaqueProtocolService.CreateOprfRequest(passwordBytes);
        if (oprfResult.IsErr)
        {
            SetError($"Failed to create OPAQUE request: {oprfResult.UnwrapErr().Message}");
            return null;
        }
        return oprfResult.Unwrap();
    }

    private async Task<Result<Unit, NetworkFailure>> SendInitRequestAndProcessResponse(
        OpaqueProtocolService clientOpaqueService,
        byte[] oprfRequest,
        BigInteger blind,
        byte[] passwordBytes)
    {
        OpaqueSignInInitRequest initRequest = new()
        {
            PhoneNumber = MobileNumber,
            PeerOprf = ByteString.CopyFrom(oprfRequest),
        };

        return await NetworkProvider.ExecuteServiceRequestAsync(
            ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
            RcpServiceType.OpaqueSignInInitRequest,
            initRequest.ToByteArray(),
            ServiceFlowType.Single,
            async payload =>
            {
                OpaqueSignInInitResponse initResponse = Helpers.ParseFromBytes<OpaqueSignInInitResponse>(payload);

                Result<
                    (
                        OpaqueSignInFinalizeRequest Request,
                        byte[] SessionKey,
                        byte[] ServerMacKey,
                        byte[] TranscriptHash
                    ),
                    OpaqueFailure
                > finalizationResult = clientOpaqueService.CreateSignInFinalizationRequest(
                    MobileNumber,
                    passwordBytes,
                    initResponse,
                    blind
                );

                if (finalizationResult.IsErr)
                {
                    string errorMessage = $"Failed to process server response: {finalizationResult.UnwrapErr().Message}";
                    SetError(errorMessage);
                    return Result<Unit, NetworkFailure>.Err(
                        EcliptixProtocolFailure.Generic(errorMessage).ToNetworkFailure()
                    );
                }

                (
                    OpaqueSignInFinalizeRequest finalizeRequest,
                    byte[] sessionKey,
                    byte[] serverMacKey,
                    byte[] transcriptHash
                ) = finalizationResult.Unwrap();

                return await SendFinalizeRequestAndVerify(clientOpaqueService, finalizeRequest, sessionKey, serverMacKey, transcriptHash);
            }
        );
    }

    private async Task<Result<Unit, NetworkFailure>> SendFinalizeRequestAndVerify(
        OpaqueProtocolService clientOpaqueService,
        OpaqueSignInFinalizeRequest finalizeRequest,
        byte[] sessionKey,
        byte[] serverMacKey,
        byte[] transcriptHash)
    {
        return await NetworkProvider.ExecuteServiceRequestAsync(
            ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
            RcpServiceType.OpaqueSignInCompleteRequest,
            finalizeRequest.ToByteArray(),
            ServiceFlowType.Single,
            async payload2 =>
            {
                OpaqueSignInFinalizeResponse finalizeResponse = Helpers.ParseFromBytes<OpaqueSignInFinalizeResponse>(payload2);

                if (finalizeResponse.Result == OpaqueSignInFinalizeResponse.Types.SignInResult.InvalidCredentials)
                {
                    SetError(finalizeResponse.HasMessage ? finalizeResponse.Message : "Invalid phone number or password.");
                    return Result<Unit, NetworkFailure>.Err(
                        EcliptixProtocolFailure.Generic("Invalid credentials.").ToNetworkFailure()
                    );
                }

                Result<byte[], OpaqueFailure> verificationResult = clientOpaqueService.VerifyServerMacAndGetSessionKey(
                    finalizeResponse,
                    sessionKey,
                    serverMacKey,
                    transcriptHash
                );

                if (verificationResult.IsErr)
                {
                    string errorMessage = $"Server authentication failed: {verificationResult.UnwrapErr().Message}";
                    SetError(errorMessage);
                    return Result<Unit, NetworkFailure>.Err(
                        EcliptixProtocolFailure.Generic(errorMessage).ToNetworkFailure()
                    );
                }

                byte[] finalSessionKey = verificationResult.Unwrap();

                return Result<Unit, NetworkFailure>.Ok(Unit.Value);
            }
        );
    }
    
    private void SetError(string message)
    {
        PasswordErrorMessage = message;
        IsErrorVisible = true;
    }

    private void ClearError()
    {
        PasswordErrorMessage = string.Empty;
        IsErrorVisible = false;
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
            _securePasswordHandle?.Dispose();
        }
        _isDisposed = true;
    }

    ~SignInViewModel()
    {
        Dispose(false);
    }
}
