using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Ecliptix.Core.Services.Authentication.Constants;
using System.Collections.Concurrent;
using System.Reactive.Concurrency;
using Serilog;
using ReactiveUI;
using Ecliptix.Opaque.Protocol;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;

namespace Ecliptix.Core.Services.Authentication;

public class OpaqueRegistrationService(
    NetworkProvider networkProvider,
    ILocalizationService localizationService)
    : IOpaqueRegistrationService
{
    private readonly ConcurrentDictionary<Guid, uint> _activeStreams = new();

    private readonly ConcurrentDictionary<ByteString, (BigInteger Blind, byte[] OprfResponse)>
        _opaqueRegistrationState = new();

    private OpaqueProtocolService CreateOpaqueService()
    {
        try
        {
            byte[] serverPublicKeyBytes = ServerPublicKey();
            Log.Information("🔐 OPAQUE: Decoding server public key for AOT compatibility");

            Org.BouncyCastle.Math.EC.ECPoint serverPublicKeyPoint =
                OpaqueCryptoUtilities.DomainParams.Curve.DecodePoint(serverPublicKeyBytes);
            ECPublicKeyParameters serverStaticPublicKeyParam = new(
                serverPublicKeyPoint,
                OpaqueCryptoUtilities.DomainParams
            );

            Log.Information("🔐 OPAQUE: Successfully created OPAQUE service");
            return new OpaqueProtocolService(serverStaticPublicKeyParam);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "🔐 OPAQUE: Failed to create OPAQUE service - DecodePoint failed in AOT mode");
            throw new InvalidOperationException("Failed to initialize OPAQUE protocol service", ex);
        }
    }

    private byte[] ServerPublicKey() =>
        SecureByteStringInterop.WithByteStringAsSpan(
            networkProvider.ApplicationInstanceSettings.ServerPublicKey,
            span => span.ToArray());

    public async Task<Result<ByteString, string>> ValidatePhoneNumberAsync(
        string mobileNumber,
        string deviceIdentifier,
        uint connectId)
    {
        if (string.IsNullOrEmpty(mobileNumber))
        {
            return Result<ByteString, string>.Err(
                localizationService[AuthenticationConstants.MobileNumberRequiredKey]);
        }

        ValidatePhoneNumberRequest request = new()
        {
            MobileNumber = mobileNumber,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier))
        };

        TaskCompletionSource<ByteString> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.ValidatePhoneNumber,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                try
                {
                    ValidatePhoneNumberResponse response = Helpers.ParseFromBytes<ValidatePhoneNumberResponse>(payload);

                    if (response.Result == VerificationResult.InvalidPhone)
                    {
                        responseSource.TrySetException(new InvalidOperationException(response.Message));
                    }
                    else
                    {
                        responseSource.TrySetResult(response.MobileNumberIdentifier);
                    }

                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseSource.TrySetException(ex);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding($"Failed to parse response: {ex.Message}")));
                }
            }, true, CancellationToken.None);

        if (networkResult.IsErr)
        {
            return Result<ByteString, string>.Err(networkResult.UnwrapErr().Message);
        }

        try
        {
            ByteString identifier = await responseSource.Task;
            return Result<ByteString, string>.Ok(identifier);
        }
        catch (Exception ex)
        {
            return Result<ByteString, string>.Err(ex.Message);
        }
    }


    public async Task<Result<Guid, string>> InitiateOtpVerificationAsync(ByteString phoneNumberIdentifier,
        string deviceIdentifier, Action<ulong>? onCountdownUpdate = null)
    {
        if (phoneNumberIdentifier.IsEmpty)
        {
            return Result<Guid, string>.Err(
                localizationService[AuthenticationConstants.PhoneNumberIdentifierRequiredKey]);
        }

        if (string.IsNullOrEmpty(deviceIdentifier))
        {
            return Result<Guid, string>.Err(localizationService[AuthenticationConstants.DeviceIdentifierRequiredKey]);
        }

        Result<uint, NetworkFailure> protocolResult =
            await networkProvider.EnsureProtocolForTypeAsync(
                PubKeyExchangeType.ServerStreaming);

        if (protocolResult.IsErr)
        {
            Log.Error("[OPAQUE-REG] Failed to establish stream protocol: {Error}", protocolResult.UnwrapErr());
            return Result<Guid, string>.Err(
                $"{AuthenticationConstants.VerificationFailurePrefix}{protocolResult.UnwrapErr().Message}");
        }

        uint streamConnectId = protocolResult.Unwrap();
        Guid sessionIdentifier = Guid.NewGuid();
        _activeStreams.TryAdd(sessionIdentifier, streamConnectId);

        Log.Information("[OPAQUE-REG] Using stream connectId {ConnectId} for verification session {SessionId}",
            streamConnectId, sessionIdentifier);

        using CancellationTokenSource cancellationTokenSource = new();

        InitiateVerificationRequest request = new()
        {
            MobileNumberIdentifier = phoneNumberIdentifier,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier)),
            Purpose = VerificationPurpose.Registration,
            Type = InitiateVerificationRequest.Types.Type.SendOtp
        };

        Result<Unit, NetworkFailure> streamResult = await networkProvider.ExecuteReceiveStreamRequestAsync(
            streamConnectId,
            RpcServiceType.InitiateVerification,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()),
            payload =>
            {
                try
                {
                    VerificationCountdownUpdate
                        timerTick = Helpers.ParseFromBytes<VerificationCountdownUpdate>(payload);

                    if (timerTick.Status is VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed
                        or VerificationCountdownUpdate.Types.CountdownUpdateStatus.Expired
                        or VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached
                        or VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound)
                    {
                        _ = Task.Run(async () => await CleanupStreamAsync(sessionIdentifier),
                            cancellationTokenSource.Token);
                    }

                    RxApp.MainThreadScheduler.Schedule(() => onCountdownUpdate?.Invoke(timerTick.SecondsRemaining));

                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    Log.Error("[OPAQUE-REG] Failed to parse countdown update: {Error}", ex.Message);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding(
                            $"{AuthenticationConstants.NetworkFailurePrefix}{ex.Message}")));
                }
            }, true, cancellationTokenSource.Token);

        if (!streamResult.IsErr) return Result<Guid, string>.Ok(sessionIdentifier);
        _activeStreams.TryRemove(sessionIdentifier, out _);
        return Result<Guid, string>.Err(streamResult.UnwrapErr().Message);
    }

    public async Task<Result<Protobuf.Membership.Membership, string>> VerifyOtpAsync(
        string otpCode,
        string deviceIdentifier,
        uint connectId)
    {
        if (string.IsNullOrEmpty(otpCode) || otpCode.Length != 6)
        {
            return Result<Protobuf.Membership.Membership, string>.Err(
                localizationService[AuthenticationConstants.InvalidOtpCodeKey]);
        }

        VerifyCodeRequest request = new()
        {
            Code = otpCode,
            Purpose = VerificationPurpose.Registration,
            AppDeviceIdentifier = Helpers.GuidToByteString(Guid.Parse(deviceIdentifier))
        };

        TaskCompletionSource<Protobuf.Membership.Membership> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.VerifyOtp,
            SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
            {
                try
                {
                    VerifyCodeResponse response = Helpers.ParseFromBytes<VerifyCodeResponse>(payload);

                    if (response.Result == VerificationResult.Succeeded)
                    {
                        responseSource.TrySetResult(response.Membership);
                    }
                    else
                    {
                        responseSource.TrySetException(
                            new InvalidOperationException(
                                localizationService[AuthenticationConstants.InvalidOtpCodeKey]));
                    }

                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseSource.TrySetException(ex);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding($"Failed to parse response: {ex.Message}")));
                }
            }, true, CancellationToken.None);

        if (networkResult.IsErr)
        {
            return Result<Protobuf.Membership.Membership, string>.Err(networkResult.UnwrapErr().Message);
        }

        try
        {
            Protobuf.Membership.Membership membership = await responseSource.Task;
            return Result<Protobuf.Membership.Membership, string>.Ok(membership);
        }
        catch (Exception ex)
        {
            return Result<Protobuf.Membership.Membership, string>.Err(ex.Message);
        }
    }

    private async Task<Result<OpaqueRegistrationInitResponse, string>> InitiateOpaqueRegistrationAsync(
        ByteString membershipIdentifier,
        byte[] oprfRequest,
        uint connectId)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<OpaqueRegistrationInitResponse, string>.Err(
                localizationService[AuthenticationConstants.MembershipIdentifierRequiredKey]);
        }

        try
        {
            OpaqueRegistrationInitRequest request = new()
            {
                PeerOprf = ByteString.CopyFrom(oprfRequest),
                MembershipIdentifier = membershipIdentifier
            };

            TaskCompletionSource<OpaqueRegistrationInitResponse> responseSource = new();

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.OpaqueRegistrationInit,
                SecureByteStringInterop.WithByteStringAsSpan(request.ToByteString(), span => span.ToArray()), payload =>
                {
                    try
                    {
                        OpaqueRegistrationInitResponse response =
                            Helpers.ParseFromBytes<OpaqueRegistrationInitResponse>(payload);
                        responseSource.TrySetResult(response);
                        return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                    }
                    catch (Exception ex)
                    {
                        responseSource.TrySetException(ex);
                        return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding(
                                $"{AuthenticationConstants.NetworkFailurePrefix}{ex.Message}")));
                    }
                }, true, CancellationToken.None);

            if (networkResult.IsErr)
            {
                return Result<OpaqueRegistrationInitResponse, string>.Err(
                    $"{AuthenticationConstants.RegistrationFailurePrefix}{networkResult.UnwrapErr().Message}");
            }

            OpaqueRegistrationInitResponse initResponse = await responseSource.Task;
            return Result<OpaqueRegistrationInitResponse, string>.Ok(initResponse);
        }
        catch (Exception ex)
        {
            Log.Error("[OPAQUE-REG] Failed to initiate OPAQUE registration: {Error}", ex.Message);
            return Result<OpaqueRegistrationInitResponse, string>.Err(
                $"{AuthenticationConstants.RegistrationFailurePrefix}{ex.Message}");
        }
    }

    public async Task<Result<Unit, string>> CompleteRegistrationAsync(ByteString membershipIdentifier,
        SecureTextBuffer secureKey, uint connectId)
    {
        if (membershipIdentifier.IsEmpty)
        {
            return Result<Unit, string>.Err(
                localizationService[AuthenticationConstants.MembershipIdentifierRequiredKey]);
        }

        if (secureKey.Length == 0)
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SecureKeyRequiredKey]);
        }

        try
        {
            Result<(byte[] OprfRequest, BigInteger Blind), OpaqueFailure> oprfResult = default;
            secureKey.WithSecureBytes(bytes =>
            {
                oprfResult = OpaqueProtocolService.CreateOprfRequest(bytes.ToArray());
            });

            if (oprfResult.IsErr)
            {
                Log.Error("[OPAQUE-REG] Failed to create OPRF request: {Error}", oprfResult.UnwrapErr());
                return Result<Unit, string>.Err(
                    $"{AuthenticationConstants.RegistrationFailurePrefix}{oprfResult.UnwrapErr().Message}");
            }

            (byte[] oprfRequest, BigInteger blind) = oprfResult.Unwrap();

            Result<OpaqueRegistrationInitResponse, string> initResult =
                await InitiateOpaqueRegistrationAsync(membershipIdentifier, oprfRequest, connectId);

            if (initResult.IsErr)
            {
                return Result<Unit, string>.Err(initResult.UnwrapErr());
            }

            OpaqueRegistrationInitResponse initResponse = initResult.Unwrap();

            if (initResponse.Result != OpaqueRegistrationInitResponse.Types.UpdateResult.Succeeded)
            {
                string errorMessage = initResponse.Result switch
                {
                    OpaqueRegistrationInitResponse.Types.UpdateResult.InvalidCredentials =>
                        localizationService[AuthenticationConstants.InvalidCredentialsKey],
                    _ => localizationService[AuthenticationConstants.RegistrationFailedKey]
                };
                return Result<Unit, string>.Err(errorMessage);
            }

            byte[] serverOprfResponse =
                SecureByteStringInterop.WithByteStringAsSpan(initResponse.PeerOprf, span => span.ToArray());
            _opaqueRegistrationState.TryAdd(membershipIdentifier, (blind, serverOprfResponse));

            Result<byte[], OpaqueFailure> recordResult = default;
            secureKey.WithSecureBytes(passwordBytes =>
            {
                OpaqueProtocolService opaqueService = CreateOpaqueService();
                recordResult =
                    opaqueService.CreateRegistrationRecord(passwordBytes.ToArray(), serverOprfResponse,
                        blind);
            });

            if (recordResult.IsErr)
            {
                _opaqueRegistrationState.TryRemove(membershipIdentifier, out _);
                Log.Error("[OPAQUE-REG] Failed to create registration record: {Error}", recordResult.UnwrapErr());
                return Result<Unit, string>.Err(
                    $"{AuthenticationConstants.RegistrationFailurePrefix}{recordResult.UnwrapErr().Message}");
            }

            byte[] registrationRecord = recordResult.Unwrap();

            OpaqueRegistrationCompleteRequest completeRequest = new()
            {
                PeerRegistrationRecord = ByteString.CopyFrom(registrationRecord),
                MembershipIdentifier = membershipIdentifier
            };

            TaskCompletionSource<OpaqueRegistrationCompleteResponse> responseSource = new();

            Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
                connectId,
                RpcServiceType.OpaqueRegistrationComplete,
                SecureByteStringInterop.WithByteStringAsSpan(completeRequest.ToByteString(), span => span.ToArray()),
                payload =>
                {
                    try
                    {
                        OpaqueRegistrationCompleteResponse response =
                            Helpers.ParseFromBytes<OpaqueRegistrationCompleteResponse>(payload);
                        responseSource.TrySetResult(response);
                        return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                    }
                    catch (Exception ex)
                    {
                        responseSource.TrySetException(ex);
                        return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                            NetworkFailure.DataCenterNotResponding(
                                $"{AuthenticationConstants.NetworkFailurePrefix}{ex.Message}")));
                    }
                }, true, CancellationToken.None);

            if (networkResult.IsErr)
            {
                _opaqueRegistrationState.TryRemove(membershipIdentifier, out _);
                return Result<Unit, string>.Err(
                    $"{AuthenticationConstants.RegistrationFailurePrefix}{networkResult.UnwrapErr().Message}");
            }

            OpaqueRegistrationCompleteResponse completeResponse = await responseSource.Task;

            _opaqueRegistrationState.TryRemove(membershipIdentifier, out _);

            if (completeResponse.Result != OpaqueRegistrationCompleteResponse.Types.RegistrationResult.Succeeded)
            {
                return Result<Unit, string>.Err(localizationService[AuthenticationConstants.RegistrationFailedKey]);
            }

            Log.Information("[OPAQUE-REG] OPAQUE registration completed successfully");
            return Result<Unit, string>.Ok(Unit.Value);
        }
        catch (Exception ex)
        {
            _opaqueRegistrationState.TryRemove(membershipIdentifier, out _);
            Log.Error("[OPAQUE-REG] Failed to complete OPAQUE registration: {Error}", ex.Message);
            return Result<Unit, string>.Err($"{AuthenticationConstants.RegistrationFailurePrefix}{ex.Message}");
        }
    }

    public async Task<Result<Unit, string>> CleanupVerificationSessionAsync(Guid sessionIdentifier)
    {
        if (sessionIdentifier == AuthenticationConstants.EmptyGuid)
        {
            return Result<Unit, string>.Err(localizationService[AuthenticationConstants.SessionIdentifierRequiredKey]);
        }

        return await CleanupStreamAsync(sessionIdentifier);
    }

    private async Task<Result<Unit, string>> CleanupStreamAsync(Guid sessionIdentifier)
    {
        if (_activeStreams.TryRemove(sessionIdentifier, out uint streamConnectId))
        {
            Log.Information(
                "[OPAQUE-REG] Cleaning up stream protocol with connectId {ConnectId} for session {SessionId}",
                streamConnectId, sessionIdentifier);

            Result<Unit, NetworkFailure> cleanupResult =
                await networkProvider.CleanupStreamProtocolAsync(streamConnectId);

            if (!cleanupResult.IsErr) return Result<Unit, string>.Ok(Unit.Value);
            Log.Warning("[OPAQUE-REG] Failed to cleanup stream protocol: {Error}", cleanupResult.UnwrapErr());
            return Result<Unit, string>.Err(cleanupResult.UnwrapErr().Message);
        }

        Log.Debug("[OPAQUE-REG] Session {SessionId} not found in active streams", sessionIdentifier);
        return Result<Unit, string>.Ok(Unit.Value);
    }
}