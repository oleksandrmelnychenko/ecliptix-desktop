using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Ecliptix.Security.Pinning.Encryption;

public sealed class ApplicationLayerEncryptionService : IApplicationLayerEncryption
{
    private readonly IMessageSigning _messageSigning;
    private readonly EncryptionOptions _options;

    public ApplicationLayerEncryptionService(IMessageSigning messageSigning, EncryptionOptions options)
    {
        _messageSigning = messageSigning;
        _options = options;
    }

    public Task<SecuredMessage> EncryptMessageAsync(ReadOnlyMemory<byte> payload, string serverPublicKeyHex)
    {
        byte[]? aesKey = null;
        byte[]? iv = null;
        byte[]? encryptedPayload = null;
        byte[]? encryptedAesKey = null;

        try
        {
            int keySize = GetKeySize(_options.Algorithm);
            aesKey = new byte[keySize];
            iv = new byte[12];

            using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(aesKey);
                rng.GetBytes(iv);
            }

            encryptedPayload = EncryptWithAesGcm(payload.Span, aesKey, iv);
            encryptedAesKey = EncryptAesKeyAsync(aesKey, serverPublicKeyHex).Result;

            byte[] signature = _messageSigning.SignAsync(payload, _options.SigningAlgorithm).Result;

            return Task.FromResult(new SecuredMessage(
                encryptedPayload,
                encryptedAesKey,
                iv,
                signature,
                _options.Algorithm,
                _options.SigningAlgorithm));
        }
        finally
        {
            if (aesKey != null) CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    public Task<byte[]> DecryptMessageAsync(SecuredMessage securedMessage, RSA privateKey)
    {
        byte[]? aesKey = null;

        try
        {
            aesKey = privateKey.Decrypt(securedMessage.EncryptedAesKey, RSAEncryptionPadding.OaepSHA256);

            return Task.FromResult(DecryptWithAesGcm(securedMessage.EncryptedPayload, aesKey, securedMessage.IV));
        }
        finally
        {
            if (aesKey != null) CryptographicOperations.ZeroMemory(aesKey);
        }
    }

    public Task<bool> VerifyMessageSignatureAsync(ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> signature, string clientPublicKeyHex)
    {
        return _messageSigning.VerifyAsync(payload, signature, clientPublicKeyHex);
    }

    private static byte[] EncryptWithAesGcm(ReadOnlySpan<byte> plaintext, byte[] key, byte[] nonce)
    {
        byte[]? ciphertext = null;
        try
        {
            ciphertext = new byte[plaintext.Length];
            Span<byte> tag = stackalloc byte[16];

            using AesGcm aesGcm = new(key, 16);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

            byte[] result = new byte[ciphertext.Length + tag.Length];
            Buffer.BlockCopy(ciphertext, 0, result, 0, ciphertext.Length);
            tag.CopyTo(result.AsSpan(ciphertext.Length));

            return result;
        }
        finally
        {
            if (ciphertext != null) CryptographicOperations.ZeroMemory(ciphertext);
        }
    }

    private static byte[] DecryptWithAesGcm(ReadOnlySpan<byte> cipherWithTag, byte[] key, byte[] nonce)
    {
        const int tagSize = 16;
        ReadOnlySpan<byte> ciphertext = cipherWithTag[..^tagSize];
        ReadOnlySpan<byte> tag = cipherWithTag[^tagSize..];

        byte[] plaintext = new byte[ciphertext.Length];

        using AesGcm aesGcm = new(key, 16);
        aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

        return plaintext;
    }

    private static Task<byte[]> EncryptAesKeyAsync(byte[] aesKey, string serverPublicKeyHex)
    {
        using RSA rsa = RSA.Create();
        byte[] publicKeyBytes = Convert.FromHexString(serverPublicKeyHex);
        rsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

        return Task.FromResult(rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256));
    }

    private static int GetKeySize(EncryptionAlgorithm algorithm) => algorithm switch
    {
        EncryptionAlgorithm.AesGcm128 => 16,
        EncryptionAlgorithm.AesGcm192 => 24,
        EncryptionAlgorithm.AesGcm256 => 32,
        _ => 32
    };
}