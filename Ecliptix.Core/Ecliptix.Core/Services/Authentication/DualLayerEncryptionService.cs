using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Authentication;

public class DualLayerEncryptionService(ISessionKeyService sessionKeyService) : IDualLayerEncryptionService
{
    public async Task<Result<byte[], string>> EncryptHeaderAsync(EnvelopeMetadata header, uint connectId)
    {
        try
        {
            byte[]? sessionKey = await sessionKeyService.GetSessionKeyAsync(connectId);
            if (sessionKey == null)
            {
                // No session key available - return header as plaintext bytes
                Log.Debug("🔒 No session key available for connectId {ConnectId}, skipping header encryption", connectId);
                return Result<byte[], string>.Ok(header.ToByteArray());
            }
            if (sessionKey.Length != 32)
            {
                CryptographicOperations.ZeroMemory(sessionKey);
                return Result<byte[], string>.Err("Invalid session key length. Expected 32 bytes for AES-256.");
            }

            byte[] headerBytes = header.ToByteArray();
            byte[] encryptedHeader = EncryptWithSessionKey(headerBytes, sessionKey);

            CryptographicOperations.ZeroMemory(sessionKey);
            CryptographicOperations.ZeroMemory(headerBytes);

            Log.Debug("🔒 Header encrypted with session key for connectId {ConnectId}", connectId);
            return Result<byte[], string>.Ok(encryptedHeader);
        }
        catch (Exception ex)
        {
            return Result<byte[], string>.Err($"Header encryption failed: {ex.Message}");
        }
    }

    public async Task<Result<byte[], string>> EncryptHeaderAsync(byte[] headerBytes, uint connectId)
    {
        try
        {
            byte[]? sessionKey = await sessionKeyService.GetSessionKeyAsync(connectId);
            if (sessionKey == null)
            {
                // No session key available - return header bytes as-is
                Log.Debug("🔒 No session key available for connectId {ConnectId}, skipping header encryption", connectId);
                return Result<byte[], string>.Ok(headerBytes);
            }
            if (sessionKey.Length != 32)
            {
                CryptographicOperations.ZeroMemory(sessionKey);
                return Result<byte[], string>.Err("Invalid session key length. Expected 32 bytes for AES-256.");
            }

            byte[] encryptedHeader = EncryptWithSessionKey(headerBytes, sessionKey);
            CryptographicOperations.ZeroMemory(sessionKey);

            Log.Debug("🔒 Header encrypted with session key for connectId {ConnectId}", connectId);
            return Result<byte[], string>.Ok(encryptedHeader);
        }
        catch (Exception ex)
        {
            return Result<byte[], string>.Err($"Header encryption failed: {ex.Message}");
        }
    }

    public async Task<Result<EnvelopeMetadata, string>> DecryptHeaderAsync(byte[] headerBytes, uint connectId)
    {
        try
        {
            byte[]? sessionKey = await sessionKeyService.GetSessionKeyAsync(connectId);
            if (sessionKey == null)
            {
                try
                {
                    EnvelopeMetadata header = EnvelopeMetadata.Parser.ParseFrom(headerBytes);
                    Log.Debug("🔒 No session key available for connectId {ConnectId}, parsing header as plaintext", connectId);
                    return Result<EnvelopeMetadata, string>.Ok(header);
                }
                catch (InvalidProtocolBufferException ex)
                {
                    return Result<EnvelopeMetadata, string>.Err($"Failed to parse unencrypted header: {ex.Message}");
                }
            }
            if (sessionKey.Length != 32)
            {
                CryptographicOperations.ZeroMemory(sessionKey);
                return Result<EnvelopeMetadata, string>.Err("Invalid session key length. Expected 32 bytes for AES-256.");
            }

            Result<EnvelopeMetadata, string> decryptResult = DecryptWithSessionKey(headerBytes, sessionKey);
            CryptographicOperations.ZeroMemory(sessionKey);

            if (decryptResult.IsOk)
            {
                Log.Debug("🔒 Header decrypted with session key for connectId {ConnectId}", connectId);
                return decryptResult;
            }

            try
            {
                EnvelopeMetadata header = EnvelopeMetadata.Parser.ParseFrom(headerBytes);
                Log.Debug("🔒 Header decryption failed, parsing as plaintext for connectId {ConnectId}", connectId);
                return Result<EnvelopeMetadata, string>.Ok(header);
            }
            catch (InvalidProtocolBufferException ex)
            {
                return Result<EnvelopeMetadata, string>.Err($"Failed to decrypt or parse header: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            return Result<EnvelopeMetadata, string>.Err($"Header decryption failed: {ex.Message}");
        }
    }

    public async Task<Result<SecureEnvelope, string>> CreateSecureEnvelopeAsync(EnvelopeMetadata metadata, byte[] encryptedPayload, uint connectId)
    {
        try
        {
            Result<byte[], string> encryptedHeaderResult = await EncryptHeaderAsync(metadata, connectId);
            if (encryptedHeaderResult.IsErr)
                return Result<SecureEnvelope, string>.Err(encryptedHeaderResult.UnwrapErr());

            byte[] headerBytes = encryptedHeaderResult.Unwrap();

            SecureEnvelope envelope = new()
            {
                MetaData = ByteString.CopyFrom(headerBytes),
                EncryptedPayload = ByteString.CopyFrom(encryptedPayload),
                Timestamp = GetProtoTimestamp(),
                ResultCode = ByteString.CopyFrom(BitConverter.GetBytes((int)EnvelopeResultCode.Success))
            };

            return Result<SecureEnvelope, string>.Ok(envelope);
        }
        catch (Exception ex)
        {
            return Result<SecureEnvelope, string>.Err($"SecureEnvelope creation failed: {ex.Message}");
        }
    }

    public async Task<Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), string>> ProcessSecureEnvelopeAsync(SecureEnvelope secureEnvelope, uint connectId)
    {
        try
        {
            Result<EnvelopeMetadata, string> headerResult =
                await DecryptHeaderAsync(secureEnvelope.MetaData.ToByteArray(), connectId);
            if (headerResult.IsErr)
                return Result<(EnvelopeMetadata Header, byte[] EncryptedPayload), string>.Err(headerResult.UnwrapErr());

            EnvelopeMetadata header = headerResult.Unwrap();
            byte[] encryptedPayload = secureEnvelope.EncryptedPayload.ToByteArray();

            return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), string>.Ok((header, encryptedPayload));
        }
        catch (Exception ex)
        {
            return Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), string>.Err($"SecureEnvelope processing failed: {ex.Message}");
        }
    }

    public async Task<bool> IsSessionKeyAvailableAsync(uint connectId)
    {
        try
        {
            byte[]? sessionKey = await sessionKeyService.GetSessionKeyAsync(connectId);
            if (sessionKey != null)
            {
                bool isValid = sessionKey.Length == 32;
                CryptographicOperations.ZeroMemory(sessionKey);
                return isValid;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    private byte[] EncryptWithSessionKey(byte[] plaintext, byte[] sessionKey)
    {
        byte[] nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        using AesGcm aes = new(sessionKey, 16);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[16];

        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        byte[] result = new byte[12 + 16 + ciphertext.Length];
        Array.Copy(nonce, 0, result, 0, 12);
        Array.Copy(tag, 0, result, 12, 16);
        Array.Copy(ciphertext, 0, result, 28, ciphertext.Length);

        CryptographicOperations.ZeroMemory(ciphertext);
        return result;
    }

    private Result<EnvelopeMetadata, string> DecryptWithSessionKey(byte[] encryptedData, byte[] sessionKey)
    {
        try
        {
            if (encryptedData.Length < 28)
            {
                return Result<EnvelopeMetadata, string>.Err("Encrypted data too short for AES-GCM format");
            }

            byte[] nonce = new byte[12];
            byte[] tag = new byte[16];
            byte[] ciphertext = new byte[encryptedData.Length - 28];

            Array.Copy(encryptedData, 0, nonce, 0, 12);
            Array.Copy(encryptedData, 12, tag, 0, 16);
            Array.Copy(encryptedData, 28, ciphertext, 0, ciphertext.Length);

            using AesGcm aes = new(sessionKey, 16);
            byte[] plaintext = new byte[ciphertext.Length];

            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            EnvelopeMetadata header = EnvelopeMetadata.Parser.ParseFrom(plaintext);
            CryptographicOperations.ZeroMemory(plaintext);
            CryptographicOperations.ZeroMemory(ciphertext);

            return Result<EnvelopeMetadata, string>.Ok(header);
        }
        catch (CryptographicException ex)
        {
            return Result<EnvelopeMetadata, string>.Err($"Cryptographic error during header decryption: {ex.Message}");
        }
        catch (InvalidProtocolBufferException ex)
        {
            return Result<EnvelopeMetadata, string>.Err($"Failed to parse decrypted header: {ex.Message}");
        }
        catch (Exception ex)
        {
            return Result<EnvelopeMetadata, string>.Err($"Header decryption failed: {ex.Message}");
        }
    }

    private static Google.Protobuf.WellKnownTypes.Timestamp GetProtoTimestamp()
    {
        return Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow);
    }
}