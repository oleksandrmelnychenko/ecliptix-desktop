using System;
using System.Buffers;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
    private SodiumSecureMemoryHandle? _securePasswordHandle;
    private SodiumSecureMemoryHandle? _secureVerifyPasswordHandle;

    private PasswordManager? _passwordManager;

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
    
    private readonly IDisposable _mobileSubscription;

    private string VerificationSessionId { get; set; }

    public PasswordConfirmationViewModel(
        NetworkProvider networkProvider, 
        ILocalizationService localizationService,
        IScreen hostScreen
        ): base(networkProvider,localizationService)
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
            .Subscribe(mobile => { VerificationSessionId = mobile; }
            );
    }

    public void UpdatePassword(string? passwordText)
    {
        _securePasswordHandle?.Dispose();
        _securePasswordHandle = null;

        if (!string.IsNullOrEmpty(passwordText))
        {
            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>
                result = ConvertStringToSodiumHandle(passwordText);
            if (result.IsOk)
            {
                _securePasswordHandle = result.Unwrap();
            }
            else
            {
                _securePasswordHandle = null;
                PasswordErrorMessage = $"Error processing password: {result.UnwrapErr().Message}";
                IsPasswordErrorVisible = true;
            }
        }

        ValidatePasswords();
    }

    public void UpdateVerifyPassword(string? passwordText)
    {
        _secureVerifyPasswordHandle?.Dispose();
        _secureVerifyPasswordHandle = null;

        if (!string.IsNullOrEmpty(passwordText))
        {
            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>
                result = ConvertStringToSodiumHandle(passwordText);
            if (result.IsOk)
            {
                _secureVerifyPasswordHandle = result.Unwrap();
            }
            else
            {
                _secureVerifyPasswordHandle = null;
                PasswordErrorMessage = $"Error processing confirmation password: {result.UnwrapErr().Message}";
                IsPasswordErrorVisible = true;
            }
        }

        ValidatePasswords();
    }

    private static Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> ConvertStringToSodiumHandle(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return SodiumSecureMemoryHandle.Allocate(0).MapSodiumFailure();
        }

        byte[]? rentedBuffer = null;
        SodiumSecureMemoryHandle? newHandle = null;
        int bytesWritten = 0;

        try
        {
            int maxByteCount = Encoding.UTF8.GetMaxByteCount(text.Length);
            rentedBuffer = ArrayPool<byte>.Shared.Rent(maxByteCount);
            bytesWritten = Encoding.UTF8.GetBytes(text, rentedBuffer);

            Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure> allocateResult =
                SodiumSecureMemoryHandle.Allocate(bytesWritten).MapSodiumFailure();

            if (allocateResult.IsErr)
            {
                return allocateResult;
            }

            newHandle = allocateResult.Unwrap();

            Result<Unit, EcliptixProtocolFailure> writeResult =
                newHandle.Write(rentedBuffer.AsSpan(0, bytesWritten)).MapSodiumFailure();
            if (writeResult.IsErr)
            {
                newHandle.Dispose();
                return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(writeResult.UnwrapErr());
            }

            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Ok(newHandle);
        }
        catch (EncoderFallbackException ex)
        {
            newHandle?.Dispose();
            return Result<SodiumSecureMemoryHandle, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode("Failed to encode password string to UTF-8 bytes.", ex));
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

    private void ValidatePasswords()
    {
        PasswordErrorMessage = string.Empty;
        IsPasswordErrorVisible = false;
        CanSubmit = false;

        bool isPasswordEntered = _securePasswordHandle is { IsInvalid: false, Length: > 0 };
        bool isVerifyPasswordEntered = _secureVerifyPasswordHandle is { IsInvalid: false, Length: > 0 };

        if (!isPasswordEntered)
        {
            if (isVerifyPasswordEntered)
            {
                PasswordErrorMessage = "Please enter your password in the first field.";
                IsPasswordErrorVisible = true;
            }

            return;
        }

        byte[]? rentedPasswordBytes = null;
        try
        {
            rentedPasswordBytes = ArrayPool<byte>.Shared.Rent(_securePasswordHandle!.Length);
            Span<byte> passwordSpan = rentedPasswordBytes.AsSpan(0, _securePasswordHandle.Length);

            Result<Unit, EcliptixProtocolFailure> readResult =
                _securePasswordHandle.Read(passwordSpan).MapSodiumFailure();
            if (readResult.IsErr)
            {
                PasswordErrorMessage = $"Error processing password: {readResult.UnwrapErr().Message}";
                IsPasswordErrorVisible = true;
                return;
            }

            string passwordString = Encoding.UTF8.GetString(passwordSpan);

            _passwordManager ??= PasswordManager.Create().Unwrap();

            Result<Unit, EcliptixProtocolFailure> complianceResult =
                _passwordManager.CheckPasswordCompliance(passwordString, PasswordPolicy.Default);

            passwordSpan.Clear();
            ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            rentedPasswordBytes = null;

            if (complianceResult.IsErr)
            {
                PasswordErrorMessage = complianceResult.UnwrapErr().Message;
                IsPasswordErrorVisible = true;
                return;
            }

            if (!isVerifyPasswordEntered)
            {
                PasswordErrorMessage = "Please verify your password.";
                IsPasswordErrorVisible = true;
                return;
            }

            Result<bool, EcliptixProtocolFailure> comparisonResult =
                CompareSodiumHandles(_securePasswordHandle, _secureVerifyPasswordHandle!);

            if (comparisonResult.IsErr)
            {
                PasswordErrorMessage = $"Error comparing passwords: {comparisonResult.UnwrapErr().Message}";
                IsPasswordErrorVisible = true;
                return;
            }

            if (!comparisonResult.Unwrap())
            {
                PasswordErrorMessage = "Passwords do not match.";
                IsPasswordErrorVisible = true;
                return;
            }

            IsPasswordErrorVisible = false;
            PasswordErrorMessage = string.Empty;
            CanSubmit = true;
        }
        catch (DecoderFallbackException ex)
        {
            PasswordErrorMessage = "Password contains invalid characters for string conversion.";
            IsPasswordErrorVisible = true;
        }
        catch (Exception ex)
        {
            PasswordErrorMessage = $"An unexpected error occurred during validation: {ex.Message}";
            IsPasswordErrorVisible = true;
        }
        finally
        {
            if (rentedPasswordBytes != null)
            {
                Span<byte> spanToClear =
                    rentedPasswordBytes.AsSpan(0, _securePasswordHandle?.Length ?? rentedPasswordBytes.Length);
                spanToClear.Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }
        }
    }

    private static Result<bool, EcliptixProtocolFailure> CompareSodiumHandles(
        SodiumSecureMemoryHandle handle1,
        SodiumSecureMemoryHandle handle2)
    {
        if (handle1.IsInvalid || handle2.IsInvalid)
        {
            return Result<bool, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed("Password handles are invalid for comparison."));
        }

        if (handle1.Length != handle2.Length)
        {
            return Result<bool, EcliptixProtocolFailure>.Ok(false);
        }

        if (handle1.Length == 0)
        {
            return Result<bool, EcliptixProtocolFailure>.Ok(true);
        }

        byte[]? rentedBytes1 = null;
        byte[]? rentedBytes2 = null;
        try
        {
            rentedBytes1 = ArrayPool<byte>.Shared.Rent(handle1.Length);
            Span<byte> span1 = rentedBytes1.AsSpan(0, handle1.Length);
            Result<Unit, EcliptixProtocolFailure> read1Result = handle1.Read(span1).MapSodiumFailure();
            if (read1Result.IsErr) return Result<bool, EcliptixProtocolFailure>.Err(read1Result.UnwrapErr());

            rentedBytes2 = ArrayPool<byte>.Shared.Rent(handle2.Length);
            Span<byte> span2 = rentedBytes2.AsSpan(0, handle2.Length);
            Result<Unit, EcliptixProtocolFailure> read2Result = handle2.Read(span2).MapSodiumFailure();
            if (read2Result.IsErr) return Result<bool, EcliptixProtocolFailure>.Err(read2Result.UnwrapErr());

            bool areEqual = CryptographicOperations.FixedTimeEquals(span1, span2);
            return Result<bool, EcliptixProtocolFailure>.Ok(areEqual);
        }
        finally
        {
            if (rentedBytes1 != null)
            {
                rentedBytes1.AsSpan(0, handle1.Length).Clear();
                ArrayPool<byte>.Shared.Return(rentedBytes1);
            }

            if (rentedBytes2 != null)
            {
                rentedBytes2.AsSpan(0, handle2.Length).Clear();
                ArrayPool<byte>.Shared.Return(rentedBytes2);
            }
        }
    }

    private async Task SubmitRegistrationPasswordAsync()
    {
        if (!CanSubmit || _securePasswordHandle is null || _securePasswordHandle.IsInvalid ||
            _securePasswordHandle.Length == 0)
        {
            PasswordErrorMessage = "Submission requirements not met.";
            IsPasswordErrorVisible = true;
            return;
        }

        IsBusy = true;
        PasswordErrorMessage = string.Empty;
        IsPasswordErrorVisible = false;

        byte[]? rentedPasswordBytes = null;
        byte[]? localSaltForEncryption = null;
        byte[]? localDataEncryptionKey = null;

        try
        {
            rentedPasswordBytes = ArrayPool<byte>.Shared.Rent(_securePasswordHandle.Length);
            Span<byte> passwordSpan = rentedPasswordBytes.AsSpan(0, _securePasswordHandle.Length);
            Result<Unit, EcliptixProtocolFailure> readResult =
                _securePasswordHandle.Read(passwordSpan).MapSodiumFailure();
            if (readResult.IsErr)
            {
                throw new InvalidOperationException(
                    $"Failed to read password for submission: {readResult.UnwrapErr().Message}");
            }

            string passwordString = Encoding.UTF8.GetString(passwordSpan);

            Result<string, EcliptixProtocolFailure> verifierResult = _passwordManager!.HashPassword(passwordString);
            if (verifierResult.IsErr)
            {
                throw new InvalidOperationException(
                    $"Failed to hash password for server: {verifierResult.UnwrapErr().Message}");
            }

            Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> opfrResult =
                OpaqueProtocolService.CreateOprfRequest(passwordSpan.ToArray());

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

            byte[] pas = passwordSpan.ToArray();

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
                        Result<byte[], OpaqueFailure> envelope = OpaqueProtocolService.CreateRegistrationRecord(pas,
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

                                //SAFE the STATE with a KEY
                                
                                return Task.FromResult(
                                    Result<Unit, NetworkFailure>.Ok(Unit.Value));
                            });
                    }

                    return Result<Unit, NetworkFailure>.Ok(Unit.Value);
                },
                CancellationToken.None
            );
        }
        catch (Exception ex)
        {
            PasswordErrorMessage = $"Submission failed: {ex.Message}";
            IsPasswordErrorVisible = true;
        }
        finally
        {
            if (rentedPasswordBytes != null)
            {
                rentedPasswordBytes.AsSpan(0, _securePasswordHandle?.Length ?? 0).Clear();
                ArrayPool<byte>.Shared.Return(rentedPasswordBytes);
            }

            if (localSaltForEncryption != null)
                Array.Clear(localSaltForEncryption, 0,
                    localSaltForEncryption.Length);
            if (localDataEncryptionKey != null) Array.Clear(localDataEncryptionKey, 0, localDataEncryptionKey.Length);

            IsBusy = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _securePasswordHandle?.Dispose();
            _secureVerifyPasswordHandle?.Dispose();
            _securePasswordHandle = null;
            _secureVerifyPasswordHandle = null;
        }

        base.Dispose(disposing);
    }

    public string? UrlPathSegment { get; } = "/password-confirmation";
    public IScreen HostScreen { get; }
}