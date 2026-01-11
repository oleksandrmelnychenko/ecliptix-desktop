using System.Security.Cryptography;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protobuf.Transport.DeviceProvisioning;
using Ecliptix.Protected.Protocol.Native;
using Ecliptix.Protected.Protocol.Utilities;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

public sealed partial class NetworkProvider
{

    private async Task<Result<byte[], NetworkFailure>> FetchPerConnectionKyberKeyAsync(
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        Log.Debug("[SECURITY] Fetching fresh per-connection Kyber key for connectId {ConnectId}", connectId);

        Result<GetServerPublicKeysResponse, NetworkFailure> rpcResult =
            await _dependencies.RpcServiceManager.GetServerPublicKeysAsync(
                _services.ConnectivityService,
                cancellationToken).ConfigureAwait(false);

        if (rpcResult.IsErr)
        {
            Log.Error("[SECURITY] Failed to fetch per-connection Kyber key: {Error}", rpcResult.UnwrapErr().Message);
            return Result<byte[], NetworkFailure>.Err(rpcResult.UnwrapErr());
        }

        GetServerPublicKeysResponse response = rpcResult.Unwrap();

        if (response.ServerKyberPublicKey.IsEmpty)
        {
            return Result<byte[], NetworkFailure>.Err(
                new NetworkFailure(
                    NetworkFailureType.KYBER_KEY_REQUIRED,
                    "Server returned empty Kyber public key"));
        }

        byte[] kyberKey = response.ServerKyberPublicKey.ToByteArray();
        Log.Debug("[SECURITY] Fetched fresh per-connection Kyber key for connectId {ConnectId}, length: {Length}",
            connectId, kyberKey.Length);

        _nativeSessions.StoreServerKyberKey(connectId, kyberKey);

        if (!response.ServerNonce.IsEmpty)
        {
            byte[] serverNonce = response.ServerNonce.ToByteArray();
            if (serverNonce.Length == AuthenticatedEstablishClientNonceLength)
            {
                _nativeSessions.StoreServerNonce(connectId, serverNonce);
                Log.Debug("[SECURITY] Stored server nonce for connectId {ConnectId}, length: {Length}",
                    connectId, serverNonce.Length);
            }
            else
            {
                Log.Warning("[SECURITY] Ignoring server nonce with invalid length ({Length}, expected {ExpectedLength}) for connectId {ConnectId}",
                    serverNonce.Length, AuthenticatedEstablishClientNonceLength, connectId);
                _nativeSessions.ClearServerNonce(connectId);
            }
        }
        else
        {
            Log.Warning("[SECURITY] Server nonce missing in GetServerPublicKeys response for connectId {ConnectId}",
                connectId);
            _nativeSessions.ClearServerNonce(connectId);
        }

        return Result<byte[], NetworkFailure>.Ok(kyberKey);
    }

    private async Task<Result<(SecureEnvelope Envelope, CertificatePinningService Service), NetworkFailure>>
        PrepareSecrecyChannelEnvelopeAsync(uint connectId, PubKeyExchangeType exchangeType)
    {
        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult = _nativeSessions.Get(connectId);
        if (nativeSessionResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(
                nativeSessionResult.UnwrapErr().ToNetworkFailure());
        }

        NativeProtocolSession nativeSession = nativeSessionResult.Unwrap();

        Log.Debug("[SECURITY] PrepareSecrecyChannelEnvelopeAsync - Fetching fresh per-connection Kyber key for connectId {ConnectId}", connectId);

        Result<byte[], NetworkFailure> kyberResult = await FetchPerConnectionKyberKeyAsync(connectId).ConfigureAwait(false);
        if (kyberResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(kyberResult.UnwrapErr());
        }

        byte[] serverKyberKey = kyberResult.Unwrap();
        Log.Debug("[SECURITY] Using fresh per-connection Kyber key, length: {Length}", serverKyberKey.Length);
        Result<byte[], EcliptixProtocolFailure> handshakeResult =
            nativeSession.BeginHandshakeWithPeerKyber(connectId, (byte)exchangeType, serverKyberKey);

        if (handshakeResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(
                handshakeResult.UnwrapErr().ToNetworkFailure());
        }

        Result<PubKeyExchange, NetworkFailure> updatedHandshakeResult =
            EnsureLocalKyberPublicKeyInHandshake(handshakeResult.Unwrap(), nativeSession);
        if (updatedHandshakeResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(
                updatedHandshakeResult.UnwrapErr());
        }

        byte[] handshakeBytes = updatedHandshakeResult.Unwrap().ToByteArray();

        Option<CertificatePinningService> certificatePinningService =
            await _security.CertificatePinningServiceFactory.GetOrInitializeServiceAsync();

        if (!certificatePinningService.IsSome)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
        }

        Result<byte[], NetworkFailure> encryptResult =
            _security.RsaChunkEncryptor.EncryptInChunks(certificatePinningService.Value!, handshakeBytes);
        if (encryptResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(encryptResult.UnwrapErr());
        }

        byte[] combinedEncryptedPayload = encryptResult.Unwrap();

        EnvelopeMetadata metadata = EnvelopeBuilder.CreateEnvelopeMetadata(
            requestId: connectId,
            nonce: ByteString.Empty,
            ratchetIndex: 0,
            envelopeType: EnvelopeType.Request
        );

        SecureEnvelope envelope = EnvelopeBuilder.CreateSecureEnvelope(
            metadata,
            ByteString.CopyFrom(combinedEncryptedPayload)
        );

        return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Ok((envelope,
            certificatePinningService.Value!));
    }

    private Result<PubKeyExchange, NetworkFailure> EnsureLocalKyberPublicKeyInHandshake(
        byte[] handshakeBytes,
        NativeProtocolSession nativeSession)
    {
        PubKeyExchange pubKeyExchange;
        try
        {
            pubKeyExchange = PubKeyExchange.Parser.ParseFrom(handshakeBytes);
        }
        catch (InvalidProtocolBufferException ex)
        {
            EcliptixProtocolFailure failure = EcliptixProtocolFailure.Decode(
                "Failed to parse PubKeyExchange handshake message.",
                ex);
            return Result<PubKeyExchange, NetworkFailure>.Err(failure.ToNetworkFailure());
        }

        if (pubKeyExchange.Payload.IsEmpty)
        {
            return Result<PubKeyExchange, NetworkFailure>.Ok(pubKeyExchange);
        }

        PublicKeyBundle bundle;
        try
        {
            bundle = PublicKeyBundle.Parser.ParseFrom(pubKeyExchange.Payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            EcliptixProtocolFailure failure = EcliptixProtocolFailure.Decode(
                "Failed to parse PublicKeyBundle from handshake payload.",
                ex);
            return Result<PubKeyExchange, NetworkFailure>.Err(failure.ToNetworkFailure());
        }

        if (!bundle.KyberPublicKey.IsEmpty)
        {
            Result<byte[], EcliptixProtocolFailure> localKyberResult =
                nativeSession.GetIdentityKeys().GetPublicKyber();
            if (localKyberResult.IsErr)
            {
                return Result<PubKeyExchange, NetworkFailure>.Ok(pubKeyExchange);
            }

            byte[] localKyberKey = localKyberResult.Unwrap();
            if (bundle.KyberPublicKey.Length == localKyberKey.Length &&
                bundle.KyberPublicKey.Span.SequenceEqual(localKyberKey))
            {
                return Result<PubKeyExchange, NetworkFailure>.Ok(pubKeyExchange);
            }

            bundle.KyberPublicKey = ByteString.CopyFrom(localKyberKey);
            pubKeyExchange.Payload = bundle.ToByteString();

            return Result<PubKeyExchange, NetworkFailure>.Ok(pubKeyExchange);
        }

        Result<byte[], EcliptixProtocolFailure> missingKyberResult =
            nativeSession.GetIdentityKeys().GetPublicKyber();
        if (missingKyberResult.IsErr)
        {
            return Result<PubKeyExchange, NetworkFailure>.Err(missingKyberResult.UnwrapErr().ToNetworkFailure());
        }

        bundle.KyberPublicKey = ByteString.CopyFrom(missingKyberResult.Unwrap());
        pubKeyExchange.Payload = bundle.ToByteString();

        return Result<PubKeyExchange, NetworkFailure>.Ok(pubKeyExchange);

    }

    private async Task<Result<(SecureEnvelope Envelope, CertificatePinningService Service), NetworkFailure>>
        PrepareNativeHandshakeEnvelopeAsync(SecrecyChannelRequest request) =>
        await PrepareSecrecyChannelEnvelopeAsync(
            request.ConnectId,
            request.ExchangeType).ConfigureAwait(false);

    private Result<PubKeyExchange, NetworkFailure> ProcessNativeHandshakeResponse(
        SecureEnvelope responseEnvelope,
        CertificatePinningService certificatePinningService,
        SecrecyChannelRequest request)
    {
        Result<NativeProtocolSession, EcliptixProtocolFailure> nativeSessionResult =
            _nativeSessions.Get(request.ConnectId);
        if (nativeSessionResult.IsErr)
        {
            return Result<PubKeyExchange, NetworkFailure>.Err(nativeSessionResult.UnwrapErr().ToNetworkFailure());
        }

        Result<byte[], NetworkFailure> decryptResult =
            _security.RsaChunkEncryptor.DecryptInChunks(certificatePinningService,
                responseEnvelope.EncryptedPayload.ToByteArray());
        if (decryptResult.IsErr)
        {
            return Result<PubKeyExchange, NetworkFailure>.Err(decryptResult.UnwrapErr());
        }

        PubKeyExchange peerPubKeyExchange = PubKeyExchange.Parser.ParseFrom(decryptResult.Unwrap());

        if (!peerPubKeyExchange.Payload.IsEmpty)
        {
            try
            {
                PublicKeyBundle serverBundle = PublicKeyBundle.Parser.ParseFrom(peerPubKeyExchange.Payload);
                if (!serverBundle.KyberPublicKey.IsEmpty)
                {
                    _nativeSessions.StoreServerKyberKey(request.ConnectId, serverBundle.KyberPublicKey.ToByteArray());
                    Log.Debug("[SECURITY] Stored per-connection Kyber key for connectId {ConnectId}, length: {Length}",
                        request.ConnectId, serverBundle.KyberPublicKey.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[SECURITY] Failed to extract server Kyber key from response: {Error}", ex.Message);

            }
        }

        Result<Unit, EcliptixProtocolFailure> completeResult =
            nativeSessionResult.Unwrap().CompleteHandshakeAuto(peerPubKeyExchange.ToByteArray());
        return completeResult.IsErr
            ? Result<PubKeyExchange, NetworkFailure>.Err(completeResult.UnwrapErr().ToNetworkFailure())
            : Result<PubKeyExchange, NetworkFailure>.Ok(peerPubKeyExchange);
    }

    private static byte[] DeriveRootKeyFromMasterKey(byte[] masterKey, Guid accountId)
    {
        const string rootKeyInfo = "ecliptix-protocol-root-key";
        byte[] saltBytes = accountId.ToByteArray();
        byte[] infoBytes = System.Text.Encoding.UTF8.GetBytes($"{rootKeyInfo}:v1:{accountId}");
        byte[] rootKey = new byte[32];

        HKDF.DeriveKey(
            HashAlgorithmName.SHA512,
            ikm: masterKey,
            output: rootKey,
            salt: saltBytes,
            info: infoBytes);

        return rootKey;
    }
}
