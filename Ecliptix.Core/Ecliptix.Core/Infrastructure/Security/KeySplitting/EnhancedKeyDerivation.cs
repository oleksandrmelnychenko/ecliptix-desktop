using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Utilities;
using Konscious.Security.Cryptography;
using Serilog;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public sealed class EnhancedKeyDerivation : IEnhancedKeyDerivation
{
    private readonly IPlatformSecurityProvider _platformSecurityProvider;
    private const string KEY_CONTEXT_PREFIX = "ecliptix-session-key";

    public EnhancedKeyDerivation(IPlatformSecurityProvider platformSecurityProvider)
    {
        _platformSecurityProvider = platformSecurityProvider ?? throw new ArgumentNullException(nameof(platformSecurityProvider));
    }

    public async Task<Result<byte[], string>> DeriveEnhancedKeyAsync(
        byte[] baseKey,
        string context,
        uint connectId,
        KeyDerivationOptions? options = null)
    {
        if (baseKey == null || baseKey.Length == 0)
            return Result<byte[], string>.Err("Base key cannot be null or empty");

        if (string.IsNullOrWhiteSpace(context))
            return Result<byte[], string>.Err("Context cannot be null or empty");

        options ??= new KeyDerivationOptions();

        try
        {
            Log.Debug("Starting enhanced key derivation for connection {ConnectId}", connectId);

            byte[] salt = GenerateContextSalt(context, connectId);

            Result<byte[], string> stretchedResult = await StretchKeyAsync(baseKey, salt, options.OutputLength, options);
            if (stretchedResult.IsErr)
                return stretchedResult;

            byte[] stretchedKey = stretchedResult.Unwrap();

            byte[] expandedKey = await ExpandKeyWithHkdfAsync(stretchedKey, connectId, options.OutputLength);

            if (options.UseHardwareEntropy && _platformSecurityProvider.IsHardwareSecurityAvailable())
            {
                byte[]? hwEntropy = null;
                try
                {
                    hwEntropy = await _platformSecurityProvider.GenerateSecureRandomAsync(options.OutputLength);
                    for (int i = 0; i < expandedKey.Length && i < hwEntropy.Length; i++)
                    {
                        expandedKey[i] ^= hwEntropy[i];
                    }
                    Log.Debug("Applied hardware entropy to derived key");
                }
                finally
                {
                    if (hwEntropy != null)
                        CryptographicOperations.ZeroMemory(hwEntropy);
                }
            }

            byte[] finalKey = await ApplyAdditionalRoundsAsync(expandedKey, connectId);

            CryptographicOperations.ZeroMemory(stretchedKey);
            CryptographicOperations.ZeroMemory(expandedKey);

            Log.Information("Successfully derived enhanced key for connection {ConnectId}", connectId);
            return Result<byte[], string>.Ok(finalKey);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to derive enhanced key for connection {ConnectId}", connectId);
            return Result<byte[], string>.Err($"Key derivation failed: {ex.Message}");
        }
    }

    public async Task<Result<byte[], string>> StretchKeyAsync(
        byte[] input,
        byte[] salt,
        int outputLength,
        KeyDerivationOptions? options = null)
    {
        if (input == null || input.Length == 0)
            return Result<byte[], string>.Err("Input cannot be null or empty");

        if (salt == null || salt.Length == 0)
            return Result<byte[], string>.Err("Salt cannot be null or empty");

        options ??= new KeyDerivationOptions();

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
                Log.Debug("Completed Argon2id key stretching with {MemorySize}KB memory", options.MemorySize);
                return Result<byte[], string>.Ok(hash);
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Argon2id key stretching failed");
            return Result<byte[], string>.Err($"Key stretching failed: {ex.Message}");
        }
    }

    public byte[] GenerateContextSalt(string context, uint connectId)
    {
        // CRITICAL FIX: Make salt deterministic for key reconstruction
        // Salt must be reproducible given same context and connectId
        string saltInput = $"{KEY_CONTEXT_PREFIX}:{context}:{connectId}";
        byte[] saltBytes = SHA256.HashData(Encoding.UTF8.GetBytes(saltInput));

        // Return the full 32-byte SHA256 hash as the salt
        // No timestamp or other non-deterministic elements
        return saltBytes;
    }

    private async Task<byte[]> ExpandKeyWithHkdfAsync(byte[] key, uint connectId, int outputLength)
    {
        return await Task.Run(() =>
        {
            byte[] info = Encoding.UTF8.GetBytes($"{KEY_CONTEXT_PREFIX}-{connectId}");

            byte[] salt = SHA256.HashData(BitConverter.GetBytes(connectId));

            byte[] pseudoRandomKey;
            using (HMACSHA512 hmac = new(salt))
            {
                pseudoRandomKey = hmac.ComputeHash(key);
            }

            byte[] expandedKey = new byte[outputLength];
            byte[] counter = new byte[1];
            int bytesGenerated = 0;

            using HMACSHA512 expandHmac = new(pseudoRandomKey);
            byte[] previousBlock = Array.Empty<byte>();

            for (int i = 1; bytesGenerated < outputLength; i++)
            {
                counter[0] = (byte)i;

                byte[] dataToHash = new byte[previousBlock.Length + info.Length + 1];
                previousBlock.CopyTo(dataToHash, 0);
                info.CopyTo(dataToHash, previousBlock.Length);
                dataToHash[^1] = counter[0];

                byte[] currentBlock = expandHmac.ComputeHash(dataToHash);

                int bytesToCopy = Math.Min(currentBlock.Length, outputLength - bytesGenerated);
                Array.Copy(currentBlock, 0, expandedKey, bytesGenerated, bytesToCopy);

                bytesGenerated += bytesToCopy;
                previousBlock = currentBlock;
            }

            CryptographicOperations.ZeroMemory(pseudoRandomKey);
            return expandedKey;
        });
    }

    private async Task<byte[]> ApplyAdditionalRoundsAsync(byte[] key, uint connectId)
    {
        return await Task.Run(() =>
        {
            byte[] result = new byte[key.Length];
            Array.Copy(key, result, key.Length);

            for (int round = 0; round < 3; round++)
            {
                using HMACSHA512 hmac = new(result);
                byte[] roundKey = hmac.ComputeHash(Encoding.UTF8.GetBytes($"round-{round}-{connectId}"));

                for (int i = 0; i < result.Length && i < roundKey.Length; i++)
                {
                    result[i] ^= roundKey[i];
                }

                using SHA512 sha = SHA512.Create();
                byte[] temp = sha.ComputeHash(result);
                Array.Copy(temp, result, Math.Min(temp.Length, result.Length));
            }

            Log.Debug("Applied 3 additional cryptographic rounds to key");
            return result;
        });
    }
}