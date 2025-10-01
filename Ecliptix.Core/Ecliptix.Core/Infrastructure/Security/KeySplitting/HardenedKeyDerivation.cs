using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;
using Ecliptix.Utilities.Failures.Sodium;
using Konscious.Security.Cryptography;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public sealed class HardenedKeyDerivation(IPlatformSecurityProvider platformSecurityProvider) : IHardenedKeyDerivation
{
    private const string KeyContextPrefix = "ecliptix-session-key";
    private const string RoundKeyFormat = "round-{0}-{1}";
    private const int AdditionalRoundsCount = 3;
    private const int MaxInfoBufferSize = 128;
    private const int MaxPreviousBlockSize = 64;
    private const int MaxRoundBufferSize = 64;

    private async Task<Result<byte[], KeySplittingFailure>> DeriveEnhancedKeyAsync(
        byte[] baseKey,
        string context,
        KeyDerivationOptions options)
    {
        try
        {
            byte[] salt = GenerateContextSalt(context);

            Result<byte[], KeySplittingFailure> stretchedResult =
                await StretchKeyAsync(baseKey, salt, options.OutputLength, options);
            if (stretchedResult.IsErr)
                return stretchedResult;

            byte[] stretchedKey = stretchedResult.Unwrap();

            byte[] expandedKey = await ExpandKeyWithHkdfAsync(stretchedKey, options.OutputLength);

            if (options.UseHardwareEntropy && platformSecurityProvider.IsHardwareSecurityAvailable())
            {
                byte[]? hwEntropy = null;
                try
                {
                    hwEntropy = await platformSecurityProvider.GenerateSecureRandomAsync(options.OutputLength);
                    for (int i = 0; i < expandedKey.Length && i < hwEntropy.Length; i++)
                    {
                        expandedKey[i] ^= hwEntropy[i];
                    }
                }
                finally
                {
                    if (hwEntropy != null)
                        CryptographicOperations.ZeroMemory(hwEntropy);
                }
            }

            byte[] finalKey = await ApplyAdditionalRoundsAsync(expandedKey);

            CryptographicOperations.ZeroMemory(stretchedKey);
            CryptographicOperations.ZeroMemory(expandedKey);

            return Result<byte[], KeySplittingFailure>.Ok(finalKey);
        }
        catch (Exception ex)
        {
            return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.KeyDerivationFailed(ex.Message, ex));
        }
    }

    private async Task<Result<byte[], KeySplittingFailure>> StretchKeyAsync(
        byte[] input,
        byte[] salt,
        int outputLength,
        KeyDerivationOptions options)
    {
        try
        {
            return await Task.Run(() =>
            {
                using Argon2id argon2 = new(input)
                {
                    Salt = salt,
                    DegreeOfParallelism = options.DegreeOfParallelism,
                    Iterations = options.Iterations,
                    MemorySize = options.MemorySize
                };

                byte[] hash = argon2.GetBytes(outputLength);
                return Result<byte[], KeySplittingFailure>.Ok(hash);
            });
        }
        catch (Exception ex)
        {
            return Result<byte[], KeySplittingFailure>.Err(KeySplittingFailure.KeyDerivationFailed(ex.Message, ex));
        }
    }

    private static byte[] GenerateContextSalt(string context)
    {
        string saltInput = $"{KeyContextPrefix}:{context}";
        byte[] saltBytes = SHA256.HashData(Encoding.UTF8.GetBytes(saltInput));
        return saltBytes;
    }

    private static async Task<byte[]> ExpandKeyWithHkdfAsync(byte[] key, int outputLength)
    {
        return await Task.Run(() =>
        {
            Span<byte> infoBuffer = stackalloc byte[MaxInfoBufferSize];
            int infoLength = Encoding.UTF8.GetBytes(KeyContextPrefix, infoBuffer);
            ReadOnlySpan<byte> info = infoBuffer[..infoLength];

            byte[] salt = SHA256.HashData(Encoding.UTF8.GetBytes(KeyContextPrefix));

            byte[] pseudoRandomKey;
            using (HMACSHA512 hmac = new(salt))
            {
                pseudoRandomKey = hmac.ComputeHash(key);
            }

            byte[] expandedKey = new byte[outputLength];
            int bytesGenerated = 0;

            using HMACSHA512 expandHmac = new(pseudoRandomKey);
            byte[] previousBlock = [];

            int maxDataToHashSize = MaxPreviousBlockSize + MaxInfoBufferSize + 1;
            byte[] dataToHashBuffer = ArrayPool<byte>.Shared.Rent(maxDataToHashSize);

            try
            {
                for (int i = 1; bytesGenerated < outputLength; i++)
                {
                    int offset = 0;
                    previousBlock.CopyTo(dataToHashBuffer, offset);
                    offset += previousBlock.Length;
                    info.CopyTo(dataToHashBuffer.AsSpan(offset));
                    offset += info.Length;
                    dataToHashBuffer[offset] = (byte)i;
                    offset++;

                    byte[] currentBlock = expandHmac.ComputeHash(dataToHashBuffer, 0, offset);

                    int bytesToCopy = Math.Min(currentBlock.Length, outputLength - bytesGenerated);
                    Array.Copy(currentBlock, 0, expandedKey, bytesGenerated, bytesToCopy);

                    bytesGenerated += bytesToCopy;
                    previousBlock = currentBlock;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(dataToHashBuffer, clearArray: true);
            }

            CryptographicOperations.ZeroMemory(pseudoRandomKey);
            return expandedKey;
        });
    }

    private static async Task<byte[]> ApplyAdditionalRoundsAsync(byte[] key)
    {
        return await Task.Run(() =>
        {
            byte[] result = (byte[])key.Clone();

            Span<byte> roundBuffer = stackalloc byte[MaxRoundBufferSize];

            for (int round = 0; round < AdditionalRoundsCount; round++)
            {
                using HMACSHA512 hmac = new(result);
                int roundInputLength = Encoding.UTF8.GetBytes(string.Format(RoundKeyFormat, round, KeyContextPrefix), roundBuffer);
                byte[] roundKey = hmac.ComputeHash(roundBuffer[..roundInputLength].ToArray());

                for (int i = 0; i < result.Length && i < roundKey.Length; i++)
                {
                    result[i] ^= roundKey[i];
                }

                byte[] temp = SHA512.HashData(result);
                Array.Copy(temp, result, Math.Min(temp.Length, result.Length));
            }

            return result;
        });
    }

    public async Task<Result<SodiumSecureMemoryHandle, KeySplittingFailure>> DeriveEnhancedMasterKeyHandleAsync(
        SodiumSecureMemoryHandle baseKeyHandle,
        string context,
        KeyDerivationOptions options)
    {
        byte[]? baseKeyBytes = null;

        try
        {
            Result<byte[], SodiumFailure> readResult =
                baseKeyHandle.ReadBytes(baseKeyHandle.Length);
            if (readResult.IsErr)
                return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(
                    KeySplittingFailure.MemoryReadFailed(readResult.UnwrapErr().Message));

            baseKeyBytes = readResult.Unwrap();

            Result<byte[], KeySplittingFailure> deriveResult =
                await DeriveEnhancedKeyAsync(baseKeyBytes, context, options);
            if (deriveResult.IsErr)
                return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(deriveResult.UnwrapErr());

            byte[] derivedKey = deriveResult.Unwrap();

            try
            {
                Result<SodiumSecureMemoryHandle, SodiumFailure> allocateResult =
                    SodiumSecureMemoryHandle.Allocate(derivedKey.Length);
                if (allocateResult.IsErr)
                    return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(
                        KeySplittingFailure.AllocationFailed(allocateResult.UnwrapErr().Message));

                SodiumSecureMemoryHandle handle = allocateResult.Unwrap();

                Result<Unit, SodiumFailure> writeResult = handle.Write(derivedKey);
                if (writeResult.IsErr)
                {
                    handle.Dispose();
                    return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(
                        KeySplittingFailure.MemoryWriteFailed(writeResult.UnwrapErr().Message));
                }

                return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Ok(handle);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(derivedKey);
            }
        }
        catch (Exception ex)
        {
            return Result<SodiumSecureMemoryHandle, KeySplittingFailure>.Err(
                KeySplittingFailure.KeyDerivationFailed(ex.Message, ex));
        }
        finally
        {
            if (baseKeyBytes != null)
                CryptographicOperations.ZeroMemory(baseKeyBytes);
        }
    }
}