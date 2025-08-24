using System;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Ecliptix.Security.Pinning.Encryption;

public interface IApplicationLayerEncryption
{
    Task<SecuredMessage> EncryptMessageAsync(ReadOnlyMemory<byte> payload, string serverPublicKeyHex);
    Task<byte[]> DecryptMessageAsync(SecuredMessage securedMessage, RSA privateKey);
    Task<bool> VerifyMessageSignatureAsync(ReadOnlyMemory<byte> payload, ReadOnlyMemory<byte> signature, string clientPublicKeyHex);
}

public sealed record SecuredMessage(
    byte[] EncryptedPayload,
    byte[] EncryptedAesKey,
    byte[] IV,
    byte[] Signature,
    EncryptionAlgorithm Algorithm,
    SigningAlgorithm SigningAlgorithm);

public enum EncryptionAlgorithm
{
    AesGcm256,
    AesGcm192,
    AesGcm128
}

public enum SigningAlgorithm
{
    RsaSha256,
    EcdsaSha256,
    EcdsaSha384
}

public sealed record EncryptionOptions(
    EncryptionAlgorithm Algorithm = EncryptionAlgorithm.AesGcm256,
    SigningAlgorithm SigningAlgorithm = SigningAlgorithm.EcdsaSha384,
    bool SecureFirstMessageOnly = true,
    bool SecureAllMessages = false,
    TimeSpan MessageTimeout = default);