using System.Security.Cryptography;
using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Network.Infrastructure.Security.Crypto;

public sealed class RsaChunkEncryptor : IRsaChunkEncryptor
{
    private const int RSA_OPTIMAL_CHUNK_SIZE = 180;  // RSA-2048 with OAEP SHA-256: max 190 bytes, use 180 for safety
    private const int RSA_MAX_PLAINTEXT_SIZE = 190;  // 256 - 2*32 - 2 = 190 bytes max for RSA-2048 OAEP SHA-256
    private const int RSA_ENCRYPTED_CHUNK_SIZE = 256;
    private const int MAX_CHUNKED_PLAINTEXT_BYTES = 1024 * 1024;
    private const int MAX_CHUNKED_CIPHERTEXT_BYTES =
        ((MAX_CHUNKED_PLAINTEXT_BYTES + RSA_OPTIMAL_CHUNK_SIZE - 1) / RSA_OPTIMAL_CHUNK_SIZE) * RSA_ENCRYPTED_CHUNK_SIZE;

    public Result<byte[], NetworkFailure> EncryptInChunks(
        CertificatePinningService certificatePinningService,
        byte[] originalData)
    {
        ArgumentNullException.ThrowIfNull(certificatePinningService);
        ArgumentNullException.ThrowIfNull(originalData);

        if (originalData.Length > MAX_CHUNKED_PLAINTEXT_BYTES)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption(
                    $"Payload size {originalData.Length} exceeds maximum {MAX_CHUNKED_PLAINTEXT_BYTES} bytes"));
        }

        int chunkCount = (originalData.Length + RSA_OPTIMAL_CHUNK_SIZE - 1) / RSA_OPTIMAL_CHUNK_SIZE;
        long estimatedSizeLong = (long)chunkCount * RSA_ENCRYPTED_CHUNK_SIZE;
        if (estimatedSizeLong > Array.MaxLength)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Output size exceeds allowed limits"));
        }

        int estimatedSize = (int)estimatedSizeLong;
        byte[] outputBuffer = new byte[estimatedSize];

        try
        {
            int currentOffset = 0;

            for (int offset = 0; offset < originalData.Length; offset += RSA_OPTIMAL_CHUNK_SIZE)
            {
                int chunkSize = Math.Min(RSA_OPTIMAL_CHUNK_SIZE, originalData.Length - offset);
                Memory<byte> chunk = originalData.AsMemory(offset, chunkSize);

                CertificatePinningByteArrayResult chunkResult =
                    certificatePinningService.Encrypt(chunk);

                if (!chunkResult.IsSuccess)
                {
                    string errorMessage = chunkResult.Error?.Message ?? "Unknown error";
                    return Result<byte[], NetworkFailure>.Err(
                        NetworkFailure.RsaEncryption($"RSA encryption failed: {errorMessage}"));
                }

                if (chunkResult.Value == null)
                {
                    continue;
                }

                int encryptedLength = chunkResult.Value.Length;
                if (encryptedLength > RSA_ENCRYPTED_CHUNK_SIZE)
                {
                    return Result<byte[], NetworkFailure>.Err(
                        NetworkFailure.RsaEncryption(
                            $"Encrypted chunk size {encryptedLength} exceeds maximum {RSA_ENCRYPTED_CHUNK_SIZE}"));
                }

                if (currentOffset > outputBuffer.Length - encryptedLength)
                {
                    return Result<byte[], NetworkFailure>.Err(
                        NetworkFailure.RsaEncryption("Output buffer overflow"));
                }

                Buffer.BlockCopy(chunkResult.Value, 0, outputBuffer, currentOffset, encryptedLength);
                currentOffset += encryptedLength;
            }

            if (currentOffset == outputBuffer.Length)
            {
                return Result<byte[], NetworkFailure>.Ok(outputBuffer);
            }

            byte[] result = new byte[currentOffset];
            Buffer.BlockCopy(outputBuffer, 0, result, 0, currentOffset);
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
    }

    public Result<byte[], NetworkFailure> DecryptInChunks(
        CertificatePinningService certificatePinningService,
        byte[] combinedEncryptedData)
    {
        ArgumentNullException.ThrowIfNull(certificatePinningService);
        ArgumentNullException.ThrowIfNull(combinedEncryptedData);

        if (combinedEncryptedData.Length > MAX_CHUNKED_CIPHERTEXT_BYTES)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption(
                    $"Payload size {combinedEncryptedData.Length} exceeds maximum {MAX_CHUNKED_CIPHERTEXT_BYTES} bytes"));
        }

        if (combinedEncryptedData.Length % RSA_ENCRYPTED_CHUNK_SIZE != 0)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption(
                    $"Ciphertext length {combinedEncryptedData.Length} is not a multiple of block size {RSA_ENCRYPTED_CHUNK_SIZE}"));
        }

        int chunkCount = combinedEncryptedData.Length / RSA_ENCRYPTED_CHUNK_SIZE;
        long estimatedSizeLong = (long)chunkCount * RSA_MAX_PLAINTEXT_SIZE;
        if (estimatedSizeLong > Array.MaxLength)
        {
            return Result<byte[], NetworkFailure>.Err(
                NetworkFailure.RsaEncryption("Output size exceeds allowed limits"));
        }

        int estimatedSize = (int)estimatedSizeLong;
        byte[] outputBuffer = new byte[estimatedSize];

        int currentOffset = 0;

        for (int offset = 0; offset < combinedEncryptedData.Length; offset += RSA_ENCRYPTED_CHUNK_SIZE)
        {
            Memory<byte> encryptedChunk = combinedEncryptedData.AsMemory(offset, RSA_ENCRYPTED_CHUNK_SIZE);

            CertificatePinningByteArrayResult chunkDecryptResult =
                certificatePinningService.Decrypt(encryptedChunk);

            if (!chunkDecryptResult.IsSuccess)
            {
                return Result<byte[], NetworkFailure>.Err(
                    NetworkFailure.RsaEncryption(
                        $"Failed to decrypt response chunk {(offset / RSA_ENCRYPTED_CHUNK_SIZE) + 1}: {chunkDecryptResult.Error?.Message}"));
            }

            if (chunkDecryptResult.Value == null)
            {
                continue;
            }

            int decryptedLength = chunkDecryptResult.Value.Length;
            if (decryptedLength > RSA_MAX_PLAINTEXT_SIZE)
            {
                return Result<byte[], NetworkFailure>.Err(
                    NetworkFailure.RsaEncryption(
                        $"Decrypted chunk size {decryptedLength} exceeds maximum {RSA_MAX_PLAINTEXT_SIZE}"));
            }

            if (currentOffset > outputBuffer.Length - decryptedLength)
            {
                return Result<byte[], NetworkFailure>.Err(
                    NetworkFailure.RsaEncryption("Output buffer overflow"));
            }

            Buffer.BlockCopy(chunkDecryptResult.Value, 0, outputBuffer, currentOffset, decryptedLength);
            currentOffset += decryptedLength;
        }

        if (currentOffset == outputBuffer.Length)
        {
            return Result<byte[], NetworkFailure>.Ok(outputBuffer);
        }

        byte[] result = new byte[currentOffset];
        Buffer.BlockCopy(outputBuffer, 0, result, 0, currentOffset);
        return Result<byte[], NetworkFailure>.Ok(result);
    }
}
