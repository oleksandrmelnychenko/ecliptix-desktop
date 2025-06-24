using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Network;
using Ecliptix.Core.OpaqueProtocol;
using Ecliptix.Core.Protocol;
using Ecliptix.Core.Protocol.Failures;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Core.Services;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.PubKeyExchange;
using Google.Protobuf;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using ReactiveUI;
using Unit = System.Reactive.Unit;
using Utilities = Ecliptix.Core.Network.Utilities;
using ShieldUnit = Ecliptix.Core.Protocol.Utilities.Unit;

namespace Ecliptix.Core.ViewModels.Authentication;

public class SignInViewModel : ViewModelBase, IDisposable, IActivatableViewModel
{
    // --- Member Fields & Properties (Unchanged from previous fix) ---
    private readonly NetworkController _networkController;
    private readonly ILocalizationService _localizationService;
    private SodiumSecureMemoryHandle? _securePasswordHandle;
    private string _phoneNumber = "+380970177443";
    private string _errorMessage = string.Empty;
    private bool _isErrorVisible;
    private bool _isBusy;
    private bool _isPasswordSet;

    public string PhoneNumber
    {
        get => _phoneNumber;
        set => this.RaiseAndSetIfChanged(ref _phoneNumber, value);
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => this.RaiseAndSetIfChanged(ref _errorMessage, value);
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

    public bool IsPasswordSet
    {
        get => _isPasswordSet;
        private set => this.RaiseAndSetIfChanged(ref _isPasswordSet, value);
    }

    public ReactiveCommand<Unit, Unit> SignInCommand { get; }
    public ViewModelActivator Activator { get; } = new();

    public SignInViewModel(NetworkController networkController, ILocalizationService localizationService)
    {
        _networkController = networkController;
        _localizationService = localizationService;

        IObservable<bool> canExecuteSignIn = this.WhenAnyValue(
            x => x.PhoneNumber, x => x.IsPasswordSet, x => x.IsBusy,
            (phone, pwdSet, busy) => !string.IsNullOrWhiteSpace(phone) && pwdSet && !busy);

        SignInCommand = ReactiveCommand.CreateFromTask(SignInAsync, canExecuteSignIn);
    }

    // --- Core Sign-In Logic ---
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
            int passwordLength = _securePasswordHandle.Length;
            rentedPasswordBytes = ArrayPool<byte>.Shared.Rent(passwordLength);
            Span<byte> passwordSpan = rentedPasswordBytes.AsSpan(0, passwordLength);

            var readResult = _securePasswordHandle.Read(passwordSpan);
            if (readResult.IsErr)
            {
                SetError($"System error: Failed to read password securely. {readResult.UnwrapErr().Message}");
                return;
            }

            // --- OPAQUE Flow Start ---

            // Setup
            var serverStaticPublicKeyParam = new ECPublicKeyParameters(
                OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(ServerPublicKey()),
                OpaqueCryptoUtilities.DomainParams);
            var clientOpaqueService = new ClientOpaqueProtocolService(serverStaticPublicKeyParam);

            // OPAQUE Step 1: Create OPRF Request
            var oprfResult = clientOpaqueService.CreateOprfRequest(passwordSpan.ToArray());
            if (oprfResult.IsErr)
            {
                SetError($"Failed to create OPAQUE request: {oprfResult.UnwrapErr().Message}");
                return;
            }

            var (oprfRequest, blind) = oprfResult.Unwrap();

            // OPAQUE Step 2: Send Init Request and process the entire remaining flow inside the callback.
            var initRequest = new OpaqueSignInInitRequest
            {
                PhoneNumber = this.PhoneNumber,
                PeerOprf = ByteString.CopyFrom(oprfRequest)
            };

            byte[] passwordBytes = passwordSpan.ToArray();

            // This is the restored nested async invocation structure.
            var overallResult = await _networkController.ExecuteServiceAction(
                ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
                RcpServiceAction.OpaqueSignInInitRequest,
                initRequest.ToByteArray(),
                ServiceFlowType.Single,
                async payload => // First nested async lambda
                {
                    OpaqueSignInInitResponse initResponse = Utilities.ParseFromBytes<OpaqueSignInInitResponse>(payload);

                    Result<(OpaqueSignInFinalizeRequest Request, byte[] SessionKey, byte[] ServerMacKey, byte[]
                        TranscriptHash), OpaqueFailure> finalizationResult =
                        clientOpaqueService.CreateSignInFinalizationRequest(
                            this.PhoneNumber, passwordBytes, initResponse, blind);

                    if (finalizationResult.IsErr)
                    {
                        var failure = EcliptixProtocolFailure.Generic(
                            $"Failed to process server response: {finalizationResult.UnwrapErr().Message}");
                        SetError(failure.Message);
                        return Result<ShieldUnit, EcliptixProtocolFailure>.Err(
                            EcliptixProtocolFailure.Generic("Failed to process server response."));
                    }

                    var (finalizeRequest, sessionKey, serverMacKey, transcriptHash) = finalizationResult.Unwrap();

                    // OPAQUE Step 4: Send Finalization Request
                    var finalizeResult = await _networkController.ExecuteServiceAction(
                        ComputeConnectId(PubKeyExchangeType.DataCenterEphemeralConnect),
                        RcpServiceAction.OpaqueSignInCompleteRequest,
                        finalizeRequest.ToByteArray(),
                        ServiceFlowType.Single,
                        async payload2 => // Second nested async lambda
                        {
                            var finalizeResponse = Utilities.ParseFromBytes<OpaqueSignInFinalizeResponse>(payload2);

                            if (finalizeResponse.Result ==
                                OpaqueSignInFinalizeResponse.Types.SignInResult.InvalidCredentials)
                            {
                                SetError(finalizeResponse.HasMessage
                                    ? finalizeResponse.Message
                                    : "Invalid phone number or password.");
                                return Result<ShieldUnit, EcliptixProtocolFailure>.Err(
                                    EcliptixProtocolFailure.Generic("Invalid credentials."));
                            }

                            // OPAQUE Step 5: Verify Server's MAC
                            var verificationResult = clientOpaqueService.VerifyServerMacAndGetSessionKey(
                                finalizeResponse, sessionKey, serverMacKey, transcriptHash);

                            if (verificationResult.IsErr)
                            {
                                var failure = EcliptixProtocolFailure.Generic(
                                    $"Server authentication failed: {verificationResult.UnwrapErr().Message}");
                                SetError(failure.Message);
                                return Result<ShieldUnit, EcliptixProtocolFailure>.Err(failure);
                            }

                            // --- Success ---
                            var finalSessionKey = verificationResult.Unwrap();
                            // TODO: Store session key and navigate.
                            System.Diagnostics.Debug.WriteLine("Sign-in successful! Session key established.");

                            // Return success for the inner network call
                            return await Task.FromResult(
                                Result<ShieldUnit, EcliptixProtocolFailure>.Ok(ShieldUnit.Value));
                        });

                    // Propagate the result from the second network call
                    return finalizeResult;
                });

            // Handle failure of the *first* network call (e.g., transport error)
            if (overallResult.IsErr)
            {
                SetError($"Sign-in network request failed: {overallResult.UnwrapErr().Message}");
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

    private void SetError(string message)
    {
        ErrorMessage = message;
        IsErrorVisible = true;
    }

    private void ClearError()
    {
        ErrorMessage = string.Empty;
        IsErrorVisible = false;
    }

    public void UpdatePassword(string? passwordText)
    {
        _securePasswordHandle?.Dispose();
        _securePasswordHandle = null;
        IsPasswordSet = false;
        ClearError();

        if (string.IsNullOrEmpty(passwordText)) return;

        Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> result = ConvertStringToSodiumHandle(passwordText);
        if (result.IsOk)
        {
            _securePasswordHandle = result.Unwrap();
            IsPasswordSet = true;
        }
        else
        {
            SetError($"Error processing password: {result.UnwrapErr().Message}");
        }
    }

    private static Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> ConvertStringToSodiumHandle(string text)
    {
        if (string.IsNullOrEmpty(text))
            return SodiumSecureMemoryHandle.Allocate(0).MapSodiumFailure();

        byte[]? rentedBuffer = null;
        SodiumSecureMemoryHandle? newHandle = null;
        int bytesWritten = 0;
        try
        {
            int maxByteCount = Encoding.UTF8.GetMaxByteCount(text.Length);
            rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
            bytesWritten = Encoding.UTF8.GetBytes(text, 0, text.Length, rentedBuffer, 0);

            var allocateResult = SodiumSecureMemoryHandle.Allocate(bytesWritten).MapSodiumFailure();
            if (allocateResult.IsErr) return allocateResult;

            newHandle = allocateResult.Unwrap();
            var writeResult = newHandle.Write(rentedBuffer.AsSpan(0, bytesWritten)).MapSodiumFailure();

            if (writeResult.IsErr)
            {
                newHandle.Dispose();
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
            }

            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(newHandle);
        }
        catch (Exception ex)
        {
            newHandle?.Dispose();
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to convert string to secure handle.", ex));
        }
        finally
        {
            if (rentedBuffer != null)
            {
                rentedBuffer.AsSpan(0, bytesWritten).Clear();
                ArrayPool<byte>.Shared.Return(rentedBuffer);
            }
        }
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _securePasswordHandle?.Dispose();
            }

            _disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}