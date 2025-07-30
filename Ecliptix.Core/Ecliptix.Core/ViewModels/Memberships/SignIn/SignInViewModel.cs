using System;
using System.Buffers;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.System;
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
    private readonly CompositeDisposable Disposables = new();
    private bool _isDisposed;
    private readonly Subject<string> _secureKeyErrorSubject = new();
    private readonly Subject<(int index, string chars)> _insertPasswordSubject = new();
    private readonly Subject<(int index, int count)> _removePasswordSubject = new();
    private int _currentSecureKeyLength;
    private bool _hasMobileNumberBeenTouched;

    [ObservableAsProperty] public string MobileNumberError { get; }
    [ObservableAsProperty] public bool HasMobileNumberError { get; }
    [ObservableAsProperty] public bool HasSecureKeyError { get; }
    [ObservableAsProperty] public string SecureKeyError { get; }
    [Reactive] public string MobileNumber { get; set; } = string.Empty;

    public int CurrentSecureKeyLength
    {
        get => _currentSecureKeyLength;
        private set => this.RaiseAndSetIfChanged(ref _currentSecureKeyLength, value);
    }

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
        ISystemEvents systemEvents,
        NetworkProvider networkProvider,
        ILocalizationService localizationService,
        IScreen hostScreen) : base(systemEvents, networkProvider, localizationService)
    {
        HostScreen = hostScreen;

        IObservable<string> rawMobileValidation = this.WhenAnyValue(x => x.MobileNumber)
            .Select(mobileNumber =>
            {
                if (!_hasMobileNumberBeenTouched && !string.IsNullOrWhiteSpace(mobileNumber))
                    _hasMobileNumberBeenTouched = true;
                return !_hasMobileNumberBeenTouched
                    ? string.Empty
                    : MobileNumberValidator.Validate(mobileNumber, LocalizationService);
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
            .Subscribe(x => { ModifySecureKeyState(x.index, 0, x.chars); })
            .DisposeWith(Disposables);

        _removePasswordSubject
            .Subscribe(x => { ModifySecureKeyState(x.index, x.count, string.Empty); })
            .DisposeWith(Disposables);

        SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync);
        AccountRecoveryCommand = ReactiveCommand.Create(() => { });
    }


    public void InsertSecureKeyChars(int index, string chars)
    {
        if (string.IsNullOrEmpty(chars)) return;
        _insertPasswordSubject.OnNext((index, chars));
    }

    public void RemoveSecureKeyChars(int index, int count)
    {
        if (count <= 0) return;
        _removePasswordSubject.OnNext((index, count));
    }

    private static int GetUtf8ByteOffset(byte[] bytes, int byteLength, int charIndex)
    {
        int currentChar = 0;
        int currentByte = 0;
        while (currentByte < byteLength && currentChar < charIndex)
        {
            byte b = bytes[currentByte];
            switch (b)
            {
                case < 0x80:
                    currentByte += 1;
                    break;
                case < 0xE0:
                    currentByte += 2;
                    break;
                case < 0xF0:
                    currentByte += 3;
                    break;
                case < 0xF8:
                    currentByte += 4;
                    break;
                default:
                    currentByte += 1;
                    continue;
            }

            currentChar++;
        }

        return currentByte;
    }

    private void ModifySecureKeyState(int index, int removeCount, string insertChars)
    {
        byte[]? oldSecureKeyBytes = null;
        byte[]? newSecureKeyBytes = null;
        SodiumSecureMemoryHandle? newHandle = null;
        bool success = false;

        try
        {
            int oldLength = _securePasswordHandle?.Length ?? 0;
            int oldCharLength = CurrentSecureKeyLength;
            index = Math.Clamp(index, 0, oldCharLength);
            removeCount = Math.Clamp(removeCount, 0, oldCharLength - index);
            int newCharLength = oldCharLength - removeCount + insertChars.Length;

            if (oldLength > 0)
            {
                oldSecureKeyBytes = ArrayPool<byte>.Shared.Rent(oldLength);
                Result<Unit, SodiumFailure> readResult =
                    _securePasswordHandle!.Read(oldSecureKeyBytes.AsSpan(0, oldLength));
                if (readResult.IsErr)
                {
                    SystemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError,
                        readResult.UnwrapErr().Message));
                    return;
                }
            }

            ReadOnlySpan<byte> oldSpan =
                oldLength > 0 ? oldSecureKeyBytes.AsSpan(0, oldLength) : ReadOnlySpan<byte>.Empty;

            byte[] insertBytes = Encoding.UTF8.GetBytes(insertChars);

            int startByte = GetUtf8ByteOffset(oldSecureKeyBytes ?? [], oldLength, index);
            int endByte = GetUtf8ByteOffset(oldSecureKeyBytes ?? [], oldLength, index + removeCount);
            int removedByteCount = endByte - startByte;

            int newLength = oldLength - removedByteCount + insertBytes.Length;
            if (newLength >= 0)
            {
                newSecureKeyBytes = ArrayPool<byte>.Shared.Rent(newLength);
                Span<byte> newSpan = newSecureKeyBytes.AsSpan(0, newLength);

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
                SystemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError,
                    allocateResult.UnwrapErr().Message));
                return;
            }

            newHandle = allocateResult.Unwrap();

            if (newLength > 0)
            {
                Result<Unit, SodiumFailure> writeResult = newHandle.Write(newSecureKeyBytes.AsSpan(0, newLength));
                if (writeResult.IsErr)
                {
                    SystemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError,
                        allocateResult.UnwrapErr().Message));
                    return;
                }
            }

            string? passwordValidationError = ValidatePassword(newSecureKeyBytes, newLength);
            if (!string.IsNullOrEmpty(passwordValidationError))
            {
                SetSecureKeyError(passwordValidationError);
            }
            else
            {
                ClearSecureKeyError();
            }

            SodiumSecureMemoryHandle? oldHandle = _securePasswordHandle;
            _securePasswordHandle = newHandle;
            oldHandle?.Dispose();
            newHandle = null;
            CurrentSecureKeyLength = newCharLength;
            success = true;
        }
        finally
        {
            if (oldSecureKeyBytes != null) ArrayPool<byte>.Shared.Return(oldSecureKeyBytes, true);
            if (newSecureKeyBytes != null) ArrayPool<byte>.Shared.Return(newSecureKeyBytes, true);
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
            rentedPasswordBytes = ReadSecureKey();
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
                SystemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError,
                    initResult.UnwrapErr().Message));
            }
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
            (string? error, _) = SecureKeyValidator.Validate(password, LocalizationService, isSignIn: true);
            return error;
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

    private byte[]? ReadSecureKey()
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
                SystemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError,
                    readResult.UnwrapErr().Message));
                return null;
            }

            return rentedPasswordBytes;
        }
        finally
        {
            if (rentedPasswordBytes != null)
            {
                rentedPasswordBytes.AsSpan().Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }
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
        if (!oprfResult.IsErr) return oprfResult.Unwrap();
        SystemEvents.Publish(SystemStateChangedEvent.New(SystemState.FatalError,
            oprfResult.UnwrapErr().Message));
        return null;
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
        );
    }

    private void SetSecureKeyError(string message) =>
        _secureKeyErrorSubject.OnNext(message);

    private void ClearSecureKeyError() =>
        _secureKeyErrorSubject.OnNext(string.Empty);

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