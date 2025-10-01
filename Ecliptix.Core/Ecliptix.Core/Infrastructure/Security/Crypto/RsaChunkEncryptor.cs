using System;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Infrastructure.Security.Crypto;

public sealed class RsaChunkEncryptor : IRsaChunkEncryptor
{
    private const int RsaMaxChunkSize = 120;
    private const int RsaEncryptedChunkSize = 256;

    public Result<byte[], NetworkFailure> EncryptInChunks(
        CertificatePinningService certificatePinningService,
        byte[] originalData)
    {
        ArgumentNullException.ThrowIfNull(certificatePinningService);
        ArgumentNullException.ThrowIfNull(originalData);

        int chunkCount = (originalData.Length + RsaMaxChunkSize - 1) / RsaMaxChunkSize;
        int estimatedSize = chunkCount * RsaEncryptedChunkSize;
        byte[] combinedEncryptedPayload = new byte[estimatedSize];
        int currentOffset = 0;

        for (int offset = 0; offset < originalData.Length; offset += RsaMaxChunkSize)
        {
            int chunkSize = Math.Min(RsaMaxChunkSize, originalData.Length - offset);
            Memory<byte> chunk = originalData.AsMemory(offset, chunkSize);

            CertificatePinningByteArrayResult chunkResult = certificatePinningService.Encrypt(chunk);

            if (chunkResult.Error != null)
            {
                return Result<byte[], NetworkFailure>.Err(
                    NetworkFailure.RsaEncryption($"RSA encryption failed: {chunkResult.Error.Message}"));
            }

            if (chunkResult.Value != null)
            {
                int encryptedLength = chunkResult.Value.Length;
                if (currentOffset + encryptedLength > combinedEncryptedPayload.Length)
                {
                    Array.Resize(ref combinedEncryptedPayload, currentOffset + encryptedLength);
                }

                Array.Copy(chunkResult.Value, 0, combinedEncryptedPayload, currentOffset, encryptedLength);
                currentOffset += encryptedLength;
            }
        }

        if (currentOffset < combinedEncryptedPayload.Length)
        {
            Array.Resize(ref combinedEncryptedPayload, currentOffset);
        }

        return Result<byte[], NetworkFailure>.Ok(combinedEncryptedPayload);
    }

    public Result<byte[], NetworkFailure> DecryptInChunks(
        CertificatePinningService certificatePinningService,
        byte[] combinedEncryptedData)
    {
        ArgumentNullException.ThrowIfNull(certificatePinningService);
        ArgumentNullException.ThrowIfNull(combinedEncryptedData);

        int chunkCount = (combinedEncryptedData.Length + RsaEncryptedChunkSize - 1) / RsaEncryptedChunkSize;
        int estimatedSize = chunkCount * RsaMaxChunkSize;
        byte[] decryptedData = new byte[estimatedSize];
        int currentOffset = 0;

        for (int offset = 0; offset < combinedEncryptedData.Length; offset += RsaEncryptedChunkSize)
        {
            int chunkSize = Math.Min(RsaEncryptedChunkSize, combinedEncryptedData.Length - offset);
            Memory<byte> encryptedChunk = combinedEncryptedData.AsMemory(offset, chunkSize);

            CertificatePinningByteArrayResult chunkDecryptResult =
                certificatePinningService.Decrypt(encryptedChunk);

            if (!chunkDecryptResult.IsSuccess)
            {
                return Result<byte[], NetworkFailure>.Err(
                    NetworkFailure.DataCenterNotResponding(
                        $"Failed to decrypt response chunk {(offset / RsaEncryptedChunkSize) + 1}: {chunkDecryptResult.Error?.Message}"));
            }

            if (chunkDecryptResult.Value != null)
            {
                int decryptedLength = chunkDecryptResult.Value.Length;
                if (currentOffset + decryptedLength > decryptedData.Length)
                {
                    Array.Resize(ref decryptedData, currentOffset + decryptedLength);
                }

                Array.Copy(chunkDecryptResult.Value, 0, decryptedData, currentOffset, decryptedLength);
                currentOffset += decryptedLength;
            }
        }

        if (currentOffset < decryptedData.Length)
        {
            Array.Resize(ref decryptedData, currentOffset);
        }

        return Result<byte[], NetworkFailure>.Ok(decryptedData);
    }
}
