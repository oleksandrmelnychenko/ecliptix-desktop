using System;
using System.Buffers;
using System.Security.Cryptography;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Infrastructure.Security.Crypto;

public sealed class RsaChunkEncryptor : IRsaChunkEncryptor
{
    private const int RSA_MAX_CHUNK_SIZE = 120;
    private const int RSA_ENCRYPTED_CHUNK_SIZE = 256;

    public Result<byte[], NetworkFailure> EncryptInChunks(
        CertificatePinningService certificatePinningService,
        byte[] originalData)
    {
        ArgumentNullException.ThrowIfNull(certificatePinningService);
        ArgumentNullException.ThrowIfNull(originalData);

        int chunkCount = (originalData.Length + RSA_MAX_CHUNK_SIZE - 1) / RSA_MAX_CHUNK_SIZE;
        int estimatedSize = chunkCount * RSA_ENCRYPTED_CHUNK_SIZE;
        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(estimatedSize);

        try
        {
            int currentOffset = 0;

            for (int offset = 0; offset < originalData.Length; offset += RSA_MAX_CHUNK_SIZE)
            {
                int chunkSize = Math.Min(RSA_MAX_CHUNK_SIZE, originalData.Length - offset);
                Memory<byte> chunk = originalData.AsMemory(offset, chunkSize);

                CertificatePinningByteArrayResult chunkResult =
                    certificatePinningService.Encrypt(chunk);

                if (chunkResult.Error != null)
                {
                    return Result<byte[], NetworkFailure>.Err(
                        NetworkFailure.RsaEncryption($"RSA encryption failed: {chunkResult.Error.Message}"));
                }

                if (chunkResult.Value == null)
                {
                    continue;
                }

                int encryptedLength = chunkResult.Value.Length;
                if (currentOffset + encryptedLength > rentedBuffer.Length)
                {
                    byte[] newBuffer = ArrayPool<byte>.Shared.Rent(currentOffset + encryptedLength);
                    Array.Copy(rentedBuffer, 0, newBuffer, 0, currentOffset);
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                    rentedBuffer = newBuffer;
                }

                Array.Copy(chunkResult.Value, 0, rentedBuffer, currentOffset, encryptedLength);
                currentOffset += encryptedLength;
            }

            byte[] result = new byte[currentOffset];
            Array.Copy(rentedBuffer, 0, result, 0, currentOffset);
            return Result<byte[], NetworkFailure>.Ok(result);
        }
        catch (CryptographicException ex)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption($"Cryptographic error: {ex.Message}"));
        }
        catch (OutOfMemoryException ex)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption($"Out of memory encrypting data: {ex.Message}"));
        }
        catch (ArgumentException ex)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption($"Invalid encryption parameters: {ex.Message}"));
        }
        catch (Exception ex)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption($"Encryption failed: {ex.Message}"));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }

    public Result<byte[], NetworkFailure> DecryptInChunks(
        CertificatePinningService certificatePinningService,
        byte[] combinedEncryptedData)
    {
        ArgumentNullException.ThrowIfNull(certificatePinningService);
        ArgumentNullException.ThrowIfNull(combinedEncryptedData);

        int chunkCount = (combinedEncryptedData.Length + RSA_ENCRYPTED_CHUNK_SIZE - 1) / RSA_ENCRYPTED_CHUNK_SIZE;
        int estimatedSize = chunkCount * RSA_MAX_CHUNK_SIZE;
        byte[] rentedBuffer = ArrayPool<byte>.Shared.Rent(estimatedSize);

        try
        {
            int currentOffset = 0;

            for (int offset = 0; offset < combinedEncryptedData.Length; offset += RSA_ENCRYPTED_CHUNK_SIZE)
            {
                int chunkSize = Math.Min(RSA_ENCRYPTED_CHUNK_SIZE, combinedEncryptedData.Length - offset);
                Memory<byte> encryptedChunk = combinedEncryptedData.AsMemory(offset, chunkSize);

                CertificatePinningByteArrayResult chunkDecryptResult =
                    certificatePinningService.Decrypt(encryptedChunk);

                if (!chunkDecryptResult.IsSuccess)
                {
                    return Result<byte[], NetworkFailure>.Err(
                        NetworkFailure.DataCenterNotResponding(
                            $"Failed to decrypt response chunk {(offset / RSA_ENCRYPTED_CHUNK_SIZE) + 1}: {chunkDecryptResult.Error?.Message}"));
                }

                if (chunkDecryptResult.Value == null)
                {
                    continue;
                }

                int decryptedLength = chunkDecryptResult.Value.Length;
                if (currentOffset + decryptedLength > rentedBuffer.Length)
                {
                    byte[] newBuffer = ArrayPool<byte>.Shared.Rent(currentOffset + decryptedLength);
                    Array.Copy(rentedBuffer, 0, newBuffer, 0, currentOffset);
                    ArrayPool<byte>.Shared.Return(rentedBuffer);
                    rentedBuffer = newBuffer;
                }

                Array.Copy(chunkDecryptResult.Value, 0, rentedBuffer, currentOffset, decryptedLength);
                currentOffset += decryptedLength;
            }

            byte[] result = new byte[currentOffset];
            Array.Copy(rentedBuffer, 0, result, 0, currentOffset);
            return Result<byte[], NetworkFailure>.Ok(result);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rentedBuffer);
        }
    }
}
