using System.Security.Cryptography;
using Ecliptix.Protobuf.Common;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Native;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;

namespace Ecliptix.Network.Network.Core.Providers;

public sealed partial class NetworkProvider
{
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
        Result<byte[], EcliptixProtocolFailure> handshakeResult =
            nativeSession.BeginHandshake(connectId, (byte)exchangeType);

        if (handshakeResult.IsErr)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(
                handshakeResult.UnwrapErr().ToNetworkFailure());
        }

        Option<CertificatePinningService> certificatePinningService =
            await _security.CertificatePinningServiceFactory.GetOrInitializeServiceAsync();

        if (!certificatePinningService.IsSome)
        {
            return Result<(SecureEnvelope, CertificatePinningService), NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Failed to initialize certificate pinning service"));
        }

        Result<byte[], NetworkFailure> encryptResult =
            _security.RsaChunkEncryptor.EncryptInChunks(certificatePinningService.Value!, handshakeResult.Unwrap());
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
