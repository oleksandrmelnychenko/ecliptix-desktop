using System;
using System.Buffers;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Network;
using Ecliptix.Core.Network.Providers;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Membership;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using Serilog;

namespace Ecliptix.Core.ViewModels.Memberships.SignIn;

public sealed class SignInViewModel : ViewModelBase, IRoutableViewModel, IDisposable
{
    private bool _isBusy;
    private SodiumSecureMemoryHandle? _securePasswordHandle;
    private bool _isDisposed;
    private readonly Subject<string> _secureKeyErrorSubject = new();
    private readonly Subject<(int index, string chars)> _insertPasswordSubject = new();
    private readonly Subject<(int index, int count)> _removePasswordSubject = new();
    private int _currentPasswordLength;
    private bool _hasMobileNumberBeenTouched;

    [ObservableAsProperty] public string MobileNumberError { get; }
    [ObservableAsProperty] public bool HasMobileNumberError { get; }
    [ObservableAsProperty] public bool HasSecureKeyError { get; }
    [ObservableAsProperty] public string SecureKeyError { get; }

    public int CurrentPasswordLength
    {
        get => _currentPasswordLength;
        private set => this.RaiseAndSetIfChanged(ref _currentPasswordLength, value);
    }

    [Reactive] public string MobileNumber { get; set; } = string.Empty;

    public string UrlPathSegment => "/sign-in";
    public IScreen HostScreen { get; }

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

        IObservable<string> rawMobileValidation = this.WhenAnyValue(x => x.MobileNumber)
            .Select(mobileNumber =>
            {
                if (!_hasMobileNumberBeenTouched && !string.IsNullOrWhiteSpace(mobileNumber))
                    _hasMobileNumberBeenTouched = true;
                return !_hasMobileNumberBeenTouched
                    ? string.Empty
                    : MembershipValidation.Validate(ValidationType.MobileNumber, mobileNumber, LocalizationService);
            });
        rawMobileValidation
            .Scan((prev, current) => string.IsNullOrEmpty(current) ? prev : current)
            .ToPropertyEx(this, x => x.MobileNumberError);
        rawMobileValidation
            .Select(error => !string.IsNullOrEmpty(error))
            .ToPropertyEx(this, x => x.HasMobileNumberError);

        _secureKeyErrorSubject
            .Scan((prev, current) => string.IsNullOrEmpty(current) ? prev : current)
            .ToPropertyEx(this, x => x.SecureKeyError);
        _secureKeyErrorSubject
            .Select(error => !string.IsNullOrEmpty(error))
            .ToPropertyEx(this, x => x.HasSecureKeyError);

        _insertPasswordSubject
            .Subscribe(x =>
            {
                try
                {
                    ClearSecureKeyError();
                    ModifySecurePassword(x.index, 0, x.chars);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in InsertPasswordChars");
                    SetSecureKeyError("An unexpected error occurred while processing password input.");
                }
            })
            .DisposeWith(Disposables);

        _removePasswordSubject
            .Subscribe(x =>
            {
                try
                {
                    ClearSecureKeyError();
                    ModifySecurePassword(x.index, x.count, string.Empty);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in RemovePasswordChars");
                    SetSecureKeyError("An unexpected error occurred while processing password removal.");
                }
            })
            .DisposeWith(Disposables);

        SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync);
        AccountRecoveryCommand = ReactiveCommand.Create(() => { });
    }

    private readonly CompositeDisposable Disposables = new();

    public void InsertPasswordChars(int index, string chars)
    {
        if (string.IsNullOrEmpty(chars)) return;
        _insertPasswordSubject.OnNext((index, chars));
    }

    public void RemovePasswordChars(int index, int count)
    {
        if (count <= 0) return;
        _removePasswordSubject.OnNext((index, count));
    }

    private static int GetUtf8ByteOffset(byte[] bytes, int byteLength, int charIndex)
    {
        try
        {
            int currentChar = 0;
            int currentByte = 0;
            while (currentByte < byteLength && currentChar < charIndex)
            {
                byte b = bytes[currentByte];
                if (b < 0x80) currentByte += 1; // 1-byte (ASCII)
                else if (b < 0xE0) currentByte += 2; // 2-byte
                else if (b < 0xF0) currentByte += 3; // 3-byte
                else if (b < 0xF8) currentByte += 4; // 4-byte
                else
                {
                    currentByte += 1; // Skip invalid byte to avoid loop hang
                    continue;
                }

                currentChar++;
            }

            return currentByte;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in GetUtf8ByteOffset");
            return 0; // Fallback to avoid crash
        }
    }

    private void ModifySecurePassword(int index, int removeCount, string insertChars)
    {
        byte[]? oldPasswordBytes = null;
        byte[]? newPasswordBytes = null;
        SodiumSecureMemoryHandle? newHandle = null;
        bool success = false;

        try
        {
            int oldLength = _securePasswordHandle?.Length ?? 0;
            int oldCharLength = CurrentPasswordLength;
            index = Math.Clamp(index, 0, oldCharLength);
            removeCount = Math.Clamp(removeCount, 0, oldCharLength - index);
            int newCharLength = oldCharLength - removeCount + insertChars.Length;

            if (oldLength > 0)
            {
                oldPasswordBytes = ArrayPool<byte>.Shared.Rent(oldLength);
                Result<Unit, SodiumFailure> readResult =
                    _securePasswordHandle!.Read(oldPasswordBytes.AsSpan(0, oldLength));
                if (readResult.IsErr)
                {
                    SetSecureKeyError($"System Error: {readResult.UnwrapErr().Message}");
                    return;
                }
            }

            ReadOnlySpan<byte> oldSpan =
                oldLength > 0 ? oldPasswordBytes.AsSpan(0, oldLength) : ReadOnlySpan<byte>.Empty;

            byte[] insertBytes = Encoding.UTF8.GetBytes(insertChars);

            int startByte = GetUtf8ByteOffset(oldPasswordBytes ?? Array.Empty<byte>(), oldLength, index);
            int endByte = GetUtf8ByteOffset(oldPasswordBytes ?? Array.Empty<byte>(), oldLength, index + removeCount);
            int removedByteCount = endByte - startByte;

            int newLength = oldLength - removedByteCount + insertBytes.Length;
            if (newLength >= 0)
            {
                newPasswordBytes = ArrayPool<byte>.Shared.Rent(newLength);
                Span<byte> newSpan = newPasswordBytes.AsSpan(0, newLength);

                oldSpan[..startByte].CopyTo(newSpan[..startByte]);
                insertBytes.CopyTo(newSpan[startByte..(startByte + insertBytes.Length)]);
                if (endByte < oldSpan.Length)
                {
                    oldSpan[endByte..].CopyTo(newSpan[(startByte + insertBytes.Length)..]);
                }
            }

            Result<SodiumSecureMemoryHandle, SodiumFailure> allocateResult =
                SodiumSecureMemoryHandle.Allocate(newLength);
            if (allocateResult.IsErr)
            {
                SetSecureKeyError($"System Error: {allocateResult.UnwrapErr().Message}");
                return;
            }

            newHandle = allocateResult.Unwrap();

            if (newLength > 0)
            {
                Result<Unit, SodiumFailure> writeResult = newHandle.Write(newPasswordBytes.AsSpan(0, newLength));
                if (writeResult.IsErr)
                {
                    SetSecureKeyError($"System Error: {writeResult.UnwrapErr().Message}");
                    return;
                }
            }

            string? passwordValidationError = ValidatePassword(newPasswordBytes, newLength);
            if (!string.IsNullOrEmpty(passwordValidationError))
            {
                SetSecureKeyError(passwordValidationError);
            }

            SodiumSecureMemoryHandle? oldHandle = _securePasswordHandle;
            _securePasswordHandle = newHandle;
            oldHandle?.Dispose();
            newHandle = null;
            CurrentPasswordLength = newCharLength;
            success = true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in ModifySecurePassword");
            SetSecureKeyError("An unexpected error occurred while processing password.");
        }
        finally
        {
            if (oldPasswordBytes != null) ArrayPool<byte>.Shared.Return(oldPasswordBytes, true);
            if (newPasswordBytes != null) ArrayPool<byte>.Shared.Return(newPasswordBytes, true);
            if (!success && newHandle != null) newHandle.Dispose();
        }
    }

    private async Task SignInAsync()
    {
        IsBusy = true;
        ClearSecureKeyError();

        if (_securePasswordHandle == null || _securePasswordHandle.IsInvalid)
        {
            SetSecureKeyError(LocalizationService["ValidationErrors.SecureKey.Required"]);
            IsBusy = false;
            return;
        }

        byte[]? rentedPasswordBytes = null;
        try
        {
            rentedPasswordBytes = ReadPassword();
            if (rentedPasswordBytes == null)
            {
                IsBusy = false;
                return;
            }

            string? passwordValidationError = ValidatePassword(rentedPasswordBytes, _securePasswordHandle.Length);
            if (!string.IsNullOrEmpty(passwordValidationError))
            {
                SetSecureKeyError(passwordValidationError);
                IsBusy = false;
                return;
            }

            OpaqueProtocolService clientOpaqueService = CreateOpaqueService();
            (byte[] OprfRequest, BigInteger Blind)? requestTuple = CreateOprfRequest(rentedPasswordBytes);
            if (requestTuple == null)
            {
                IsBusy = false;
                return;
            }

            byte[] oprfRequest = requestTuple.Value.OprfRequest;
            BigInteger blind = requestTuple.Value.Blind;

            byte[] passwordBytes = rentedPasswordBytes.AsSpan().ToArray();

            Result<Unit, NetworkFailure> initResult =
                await SendInitRequestAndProcessResponse(clientOpaqueService, oprfRequest, blind, passwordBytes);
            if (initResult.IsErr)
            {
                SetSecureKeyError($"Sign-in init failed: {initResult.UnwrapErr().Message}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in SignInAsync");
            SetSecureKeyError($"An unexpected error occurred: {ex.Message}");
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

    private string? ValidatePassword(byte[]? passwordBytes, int length)
    {
        if (passwordBytes == null || length == 0)
        {
            return LocalizationService["ValidationErrors.SecureKey.Required"];
        }

        byte[]? rentedBytes = null;
        try
        {
            rentedBytes = ArrayPool<byte>.Shared.Rent(length);
            Array.Copy(passwordBytes, 0, rentedBytes, 0, length);
            string password = Encoding.UTF8.GetString(rentedBytes, 0, length);
            return SecureKeyValidator.Validate(password, LocalizationService);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in ValidatePassword");
            return "Invalid password format.";
        }
        finally
        {
            if (rentedBytes != null)
            {
                rentedBytes.AsSpan().Clear();
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
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
                SetSecureKeyError($"System error: Failed to read password securely. {readResult.UnwrapErr().Message}");
                return null;
            }

            return rentedPasswordBytes;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in ReadPassword");
            SetSecureKeyError("An unexpected error occurred while reading password.");
            if (rentedPasswordBytes != null)
            {
                rentedPasswordBytes.AsSpan().Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }

            return null;
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
            SetSecureKeyError($"Failed to create OPAQUE request: {oprfResult.UnwrapErr().Message}");
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
                try
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
                        string errorMessage =
                            $"Failed to process server response: {finalizationResult.UnwrapErr().Message}";
                        SetSecureKeyError(errorMessage);
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

                    return await SendFinalizeRequestAndVerify(clientOpaqueService, finalizeRequest, sessionKey,
                        serverMacKey, transcriptHash);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in SendInitRequestAndProcessResponse");
                    SetSecureKeyError("An unexpected error occurred during sign-in initialization.");
                    return Result<Unit, NetworkFailure>.Err(
                        EcliptixProtocolFailure.Generic("Sign-in initialization failed.").ToNetworkFailure());
                }
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
                try
                {
                    OpaqueSignInFinalizeResponse finalizeResponse =
                        Helpers.ParseFromBytes<OpaqueSignInFinalizeResponse>(payload2);

                    if (finalizeResponse.Result == OpaqueSignInFinalizeResponse.Types.SignInResult.InvalidCredentials)
                    {
                        SetSecureKeyError(finalizeResponse.HasMessage
                            ? finalizeResponse.Message
                            : LocalizationService["ValidationErrors.SecureKey.InvalidCredentials"]);
                        return Result<Unit, NetworkFailure>.Err(
                            EcliptixProtocolFailure.Generic("Invalid credentials.").ToNetworkFailure()
                        );
                    }

                    Result<byte[], OpaqueFailure> verificationResult =
                        clientOpaqueService.VerifyServerMacAndGetSessionKey(
                            finalizeResponse,
                            sessionKey,
                            serverMacKey,
                            transcriptHash
                        );

                    if (verificationResult.IsErr)
                    {
                        string errorMessage = $"Server authentication failed: {verificationResult.UnwrapErr().Message}";
                        SetSecureKeyError(errorMessage);
                        return Result<Unit, NetworkFailure>.Err(
                            EcliptixProtocolFailure.Generic(errorMessage).ToNetworkFailure()
                        );
                    }

                    byte[] finalSessionKey = verificationResult.Unwrap();
                    return Result<Unit, NetworkFailure>.Ok(Unit.Value);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error in SendFinalizeRequestAndVerify");
                    SetSecureKeyError("An unexpected error occurred during sign-in finalization.");
                    return Result<Unit, NetworkFailure>.Err(
                        EcliptixProtocolFailure.Generic("Sign-in finalization failed.").ToNetworkFailure());
                }
            }
        );
    }

    private void SetSecureKeyError(string message)
    {
        _secureKeyErrorSubject.OnNext(message);
    }

    private void ClearSecureKeyError()
    {
        _secureKeyErrorSubject.OnNext(string.Empty);
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
            _secureKeyErrorSubject.Dispose();
            _insertPasswordSubject.Dispose();
            _removePasswordSubject.Dispose();
            Disposables.Dispose();
        }

        _isDisposed = true;
    }

    ~SignInViewModel()
    {
        Dispose(false);
    }
}