using System;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Ecliptix.Protocol.System.Utilities;

public static class EnvelopeBuilder
{
    public static EnvelopeMetadata CreateEnvelopeMetadata(
        uint requestId,
        ByteString nonce,
        uint ratchetIndex,
        ByteString? dhPublicKey = null,
        byte[]? channelKeyId = null,
        EnvelopeType envelopeType = EnvelopeType.Request,
        string? correlationId = null)
    {
        EnvelopeMetadata metadata = new()
        {
            EnvelopeId = requestId.ToString(),
            Nonce = nonce,
            RatchetIndex = ratchetIndex,
            EnvelopeType = envelopeType
        };

        if (dhPublicKey != null && !dhPublicKey.IsEmpty)
        {
            metadata.DhPublicKey = dhPublicKey;
        }

        metadata.ChannelKeyId =
            channelKeyId is { Length: > 0 } ? ByteString.CopyFrom(channelKeyId) : GenerateChannelKeyId();

        if (!string.IsNullOrEmpty(correlationId))
        {
            metadata.CorrelationId = correlationId;
        }

        return metadata;
    }

    public static SecureEnvelope CreateSecureEnvelope(
        EnvelopeMetadata metadata,
        ByteString encryptedPayload,
        Timestamp? timestamp = null,
        ByteString? authenticationTag = null,
        EnvelopeResultCode resultCode = EnvelopeResultCode.Success,
        ByteString? errorDetails = null)
    {
        SecureEnvelope envelope = new()
        {
            MetaData = metadata.ToByteString(),
            EncryptedPayload = encryptedPayload,
            ResultCode = ByteString.CopyFrom(BitConverter.GetBytes((int)resultCode)),
            Timestamp = timestamp ?? Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        };

        if (authenticationTag != null && !authenticationTag.IsEmpty)
        {
            envelope.AuthenticationTag = authenticationTag;
        }

        if (errorDetails != null && !errorDetails.IsEmpty)
        {
            envelope.ErrorDetails = errorDetails;
        }

        return envelope;
    }

    public static Result<EnvelopeMetadata, EcliptixProtocolFailure> ParseEnvelopeMetadata(ByteString metaDataBytes)
    {
        try
        {
            SecureByteStringInterop.SecureCopyWithCleanup(metaDataBytes, out byte[] metaDataArray);
            try
            {
                EnvelopeMetadata metadata = EnvelopeMetadata.Parser.ParseFrom(metaDataArray);
                return Result<EnvelopeMetadata, EcliptixProtocolFailure>.Ok(metadata);
            }
            finally
            {
                Array.Clear(metaDataArray);
            }
        }
        catch (Exception ex)
        {
            return Result<EnvelopeMetadata, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode($"Failed to parse EnvelopeMetadata: {ex.Message}", ex));
        }
    }

    public static Result<EnvelopeResultCode, EcliptixProtocolFailure> ParseResultCode(ByteString resultCodeBytes)
    {
        try
        {
            if (resultCodeBytes.Length != sizeof(int))
            {
                return Result<EnvelopeResultCode, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Decode("Invalid result code length"));
            }

            int resultCodeValue = BitConverter.ToInt32(resultCodeBytes.Span);
            if (!global::System.Enum.IsDefined(typeof(EnvelopeResultCode), resultCodeValue))
            {
                return Result<EnvelopeResultCode, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Decode($"Unknown result code value: {resultCodeValue}"));
            }

            EnvelopeResultCode resultCode = (EnvelopeResultCode)resultCodeValue;
            return Result<EnvelopeResultCode, EcliptixProtocolFailure>.Ok(resultCode);
        }
        catch (Exception ex)
        {
            return Result<EnvelopeResultCode, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Decode($"Failed to parse result code: {ex.Message}", ex));
        }
    }

    public static uint ExtractRequestIdFromEnvelopeId(string envelopeId)
    {
        if (uint.TryParse(envelopeId, out uint requestId))
        {
            return requestId;
        }

        return Helpers.GenerateRandomUInt32(true);
    }

    private static ByteString GenerateChannelKeyId()
    {
        byte[] keyId = new byte[16];
        global::System.Security.Cryptography.RandomNumberGenerator.Fill(keyId);
        return ByteString.CopyFrom(keyId);
    }
}