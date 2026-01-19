using System.Security.Cryptography;
using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Protected.Protocol.Native;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Serilog;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

public sealed partial class NetworkProvider
{
    private async Task<Result<byte[], NetworkFailure>> FetchServerPreKeyBundleAsync(
        uint connectId,
        PubKeyExchangeType exchangeType,
        CancellationToken cancellationToken = default)
    {
        Log.Debug("[SECURITY] Fetching server prekey bundle for connectId {ConnectId}, exchangeType={ExchangeType}",
            connectId, exchangeType);

        CancellationToken finalToken = cancellationToken == CancellationToken.None
            ? GetConnectionRecoveryToken()
            : cancellationToken;

        Result<ServerPublicKeysResponse, NetworkFailure> rpcResult =
            await _services.RetryStrategy.ExecuteRpcOperationAsync(
                (_, ct) => _dependencies.RpcServiceManager.GetServerPublicKeysAsync(
                    _services.ConnectivityService,
                    exchangeType,
                    ct),
                operationName: "FetchServerPreKeyBundle",
                connectId,
                serviceType: RpcServiceType.GetServerPublicKeys,
                cancellationToken: finalToken).ConfigureAwait(false);

        if (rpcResult.IsErr)
        {
            Log.Error("[SECURITY] Failed to fetch server prekey bundle: {Error}", rpcResult.UnwrapErr().Message);
            return Result<byte[], NetworkFailure>.Err(rpcResult.UnwrapErr());
        }

        ServerPublicKeysResponse response = rpcResult.Unwrap();

        if (response.ServerPrekeyBundle.IsEmpty)
        {
            return Result<byte[], NetworkFailure>.Err(
                new NetworkFailure(
                    NetworkFailureType.KYBER_KEY_REQUIRED,
                    "Server returned empty prekey bundle"));
        }

        byte[] preKeyBundle = response.ServerPrekeyBundle.ToByteArray();
        string bundleHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(preKeyBundle))[..16];
        Log.Information("[SECURITY] Fetched server prekey bundle for connectId {ConnectId}, length: {Length}, hash: {Hash}",
            connectId, preKeyBundle.Length, bundleHash);

        _nativeSessions.StoreServerPreKeyBundle(connectId, preKeyBundle);

        if (!response.ServerPublicKey.IsEmpty)
        {
            byte[] serverPublicKey = response.ServerPublicKey.ToByteArray();
            _nativeSessions.StoreServerPublicKey(connectId, serverPublicKey);
            Log.Debug("[SECURITY] Stored server X25519 public key for connectId {ConnectId}, length: {Length}",
                connectId, serverPublicKey.Length);
        }

        if (!response.ServerNonce.IsEmpty)
        {
            byte[] serverNonce = response.ServerNonce.ToByteArray();
            if (serverNonce.Length == AUTHENTICATED_ESTABLISH_CLIENT_NONCE_LENGTH)
            {
                _nativeSessions.StoreServerNonce(connectId, serverNonce);
                Log.Debug("[SECURITY] Stored server nonce for connectId {ConnectId}, length: {Length}",
                    connectId, serverNonce.Length);
            }
            else
            {
                Log.Warning(
                    "[SECURITY] Ignoring server nonce with invalid length ({Length}, expected {ExpectedLength}) for connectId {ConnectId}",
                    serverNonce.Length, AUTHENTICATED_ESTABLISH_CLIENT_NONCE_LENGTH, connectId);
                _nativeSessions.ClearServerNonce(connectId);
            }
        }
        else
        {
            Log.Warning("[SECURITY] Server nonce missing in GetServerPublicKeys response for connectId {ConnectId}",
                connectId);
            _nativeSessions.ClearServerNonce(connectId);
        }

        return Result<byte[], NetworkFailure>.Ok(preKeyBundle);
    }

    private async Task<Result<(SecureEnvelope Envelope, CertificatePinningService Service, byte[] HandshakeInit), NetworkFailure>>
        PrepareSecrecyChannelEnvelopeAsync(uint connectId, PubKeyExchangeType exchangeType)
    {
        Result<EcliptixIdentityKeysWrapper, EcliptixProtocolFailure> identityResult =
            _nativeSessions.GetIdentity(connectId);
        if (identityResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService, byte[]), NetworkFailure>.Err(
                identityResult.UnwrapErr().ToNetworkFailure());
        }

        Log.Debug(
            "[SECURITY] PrepareSecrecyChannelEnvelopeAsync - Fetching server prekey bundle for connectId {ConnectId}, exchangeType={ExchangeType}",
            connectId, exchangeType);

        Result<byte[], NetworkFailure> bundleResult =
            await FetchServerPreKeyBundleAsync(connectId, exchangeType).ConfigureAwait(false);
        if (bundleResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService, byte[]), NetworkFailure>.Err(
                bundleResult.UnwrapErr());
        }

        Result<uint, NetworkFailure> chainLimitResult = ResolveChainLimit(exchangeType);
        if (chainLimitResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService, byte[]), NetworkFailure>.Err(
                chainLimitResult.UnwrapErr());
        }

        byte[] bundleForHandshake = bundleResult.Unwrap();
        string handshakeBundleHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bundleForHandshake))[..16];
        Log.Information("[SECURITY] Starting handshake initiator for connectId {ConnectId}, exchangeType={ExchangeType}, bundleLength={Length}, bundleHash={Hash}, chainLimit={ChainLimit}",
            connectId, exchangeType, bundleForHandshake.Length, handshakeBundleHash, chainLimitResult.Unwrap());

        Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure> handshakeStart =
            NativeHandshakeInitiator.Start(identityResult.Unwrap(), bundleForHandshake, chainLimitResult.Unwrap());
        if (handshakeStart.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService, byte[]), NetworkFailure>.Err(
                handshakeStart.UnwrapErr().ToNetworkFailure());
        }

        NativeHandshakeInitiatorStart startInfo = handshakeStart.Unwrap();
        string initHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(startInfo.HandshakeInit))[..16];
        Log.Information("[SECURITY] Created handshake init for connectId {ConnectId}, initLength={Length}, initHash={Hash}",
            connectId, startInfo.HandshakeInit.Length, initHash);
        _nativeSessions.StoreHandshakeInitiator(connectId, startInfo.Initiator);

        Option<CertificatePinningService> certificatePinningService =
            await _security.CertificatePinningServiceFactory.GetOrInitializeServiceAsync();

        if (!certificatePinningService.IsSome)
        {
            return Result<(SecureEnvelope, CertificatePinningService, byte[]), NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
        }

        Result<byte[], NetworkFailure> encryptResult =
            _security.RsaChunkEncryptor.EncryptInChunks(certificatePinningService.Value!, startInfo.HandshakeInit);
        if (encryptResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService, byte[]), NetworkFailure>.Err(
                encryptResult.UnwrapErr());
        }

        byte[] combinedEncryptedPayload = encryptResult.Unwrap();

        SecureEnvelope envelope = new()
        {
            Version = 1,
            EncryptedPayload = ByteString.CopyFrom(combinedEncryptedPayload),
            EncryptedMetadata = ByteString.Empty,
            HeaderNonce = ByteString.Empty,
            RatchetEpoch = 0,
            SentAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        return Result<(SecureEnvelope, CertificatePinningService, byte[]), NetworkFailure>.Ok((
            envelope,
            certificatePinningService.Value!,
            startInfo.HandshakeInit));
    }
    private async Task<Result<(SecureEnvelope Envelope, CertificatePinningService Service, byte[] HandshakeInit), NetworkFailure>>
        PrepareNativeHandshakeEnvelopeAsync(SecrecyChannelRequest request) =>
        await PrepareSecrecyChannelEnvelopeAsync(
            request.ConnectId,
            request.ExchangeType).ConfigureAwait(false);

    private Result<Unit, NetworkFailure> ProcessNativeHandshakeResponse(
        SecureEnvelope responseEnvelope,
        CertificatePinningService certificatePinningService,
        uint connectId)
    {
        if (responseEnvelope.EncryptedMetadata.IsEmpty)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Handshake response signature is missing"));
        }

        CertificatePinningBoolResult verifyResult = certificatePinningService.VerifyServerSignature(
            responseEnvelope.EncryptedPayload.Memory,
            responseEnvelope.EncryptedMetadata.Memory);
        if (!verifyResult.IsSuccess)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.RsaEncryption(verifyResult.Error?.Message ?? "Signature verification failed"));
        }

        if (!verifyResult.Value)
        {
            return Result<Unit, NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Handshake response signature verification failed"));
        }

        Result<byte[], NetworkFailure> decryptResult =
            _security.RsaChunkEncryptor.DecryptInChunks(certificatePinningService,
                responseEnvelope.EncryptedPayload.ToByteArray());
        if (decryptResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(decryptResult.UnwrapErr());
        }

        Result<NativeHandshakeInitiator, EcliptixProtocolFailure> initiatorResult =
            _nativeSessions.GetHandshakeInitiator(connectId);
        if (initiatorResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(initiatorResult.UnwrapErr().ToNetworkFailure());
        }

        Result<NativeProtocolSession, EcliptixProtocolFailure> finishResult =
            initiatorResult.Unwrap().Finish(decryptResult.Unwrap());
        if (finishResult.IsErr)
        {
            return Result<Unit, NetworkFailure>.Err(finishResult.UnwrapErr().ToNetworkFailure());
        }

        _nativeSessions.ClearHandshakeInitiator(connectId);
        _nativeSessions.StoreSession(connectId, finishResult.Unwrap());
        return Result<Unit, NetworkFailure>.Ok(Unit.Value);
    }

    private static Result<uint, NetworkFailure> ResolveChainLimit(PubKeyExchangeType exchangeType)
    {
        return exchangeType switch
        {
            PubKeyExchangeType.InitialHandshake => Result<uint, NetworkFailure>.Ok(20),
            PubKeyExchangeType.DataCenterEphemeralConnect => Result<uint, NetworkFailure>.Ok(20),
            PubKeyExchangeType.ServerStreaming => Result<uint, NetworkFailure>.Ok(100),
            PubKeyExchangeType.DeviceToDevice => Result<uint, NetworkFailure>.Ok(10),
            _ => Result<uint, NetworkFailure>.Err(
                NetworkFailure.InvalidRequestType($"Unsupported exchange type {exchangeType}"))
        };
    }

    private static byte[] DeriveRootKeyFromMasterKey(byte[] masterKey, Guid accountId)
    {
        const int guidSize = 16;
        const int infoPrefixLength = 28;
        const int guidStringLength = 36;
        const int totalInfoLength = infoPrefixLength + guidStringLength;

        Span<byte> salt = stackalloc byte[guidSize];
        accountId.TryWriteBytes(salt);

        Span<char> guidChars = stackalloc char[guidStringLength];
        accountId.TryFormat(guidChars, out _);

        Span<byte> info = stackalloc byte[totalInfoLength];
        "ecliptix-protocol-root-key:v1:"u8.CopyTo(info);
        System.Text.Encoding.UTF8.GetBytes(guidChars, info[infoPrefixLength..]);

        byte[] rootKey = new byte[32];

        HKDF.DeriveKey(
            HashAlgorithmName.SHA512,
            ikm: masterKey,
            output: rootKey,
            salt: salt,
            info: info);

        return rootKey;
    }
}
