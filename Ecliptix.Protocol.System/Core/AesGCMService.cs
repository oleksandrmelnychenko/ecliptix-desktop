using System.Security.Cryptography;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Core;

public static class AesGcmService
{
    private const string ErrInvalidKeyLength = "Invalid AES key length";
    private const string ErrInvalidNonceLength = "Invalid AES-GCM nonce length";
    private const string ErrInvalidTagLength = "Invalid AES-GCM tag length";
    private const string ErrEncryptFail = "AES-GCM encryption failed";
    private const string ErrDecryptFail = "AES-GCM decryption failed (authentication tag mismatch)";
    private const string ErrBufferTooSmall = "Destination buffer is too small";
    private const string ErrBufferOverlap = "Source and destination buffers overlap (not supported for in-place operations)";

    private static void Encrypt(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        Span<byte> ciphertextDestination,
        Span<byte> tagDestination,
        ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != Constants.AesKeySize) throw new ArgumentException(ErrInvalidKeyLength, nameof(key));
        if (nonce.Length != Constants.AesGcmNonceSize)
            throw new ArgumentException(ErrInvalidNonceLength, nameof(nonce));
        if (tagDestination.Length != Constants.AesGcmTagSize)
            throw new ArgumentException(ErrInvalidTagLength, nameof(tagDestination));
        if (ciphertextDestination.Length < plaintext.Length)
            throw new ArgumentException(ErrBufferTooSmall, nameof(ciphertextDestination));
        if (ciphertextDestination.Overlaps(plaintext))
            throw new ArgumentException(ErrBufferOverlap, nameof(ciphertextDestination));

        try
        {
            using AesGcm aesGcm = new(key, Constants.AesGcmTagSize);
            aesGcm.Encrypt(nonce, plaintext, ciphertextDestination, tagDestination, associatedData);
        }
        catch (CryptographicException cryptoEx)
        {
            throw new ProtocolChainStepException(ErrEncryptFail, cryptoEx);
        }
        catch (Exception ex)
        {
            throw new ProtocolChainStepException(ErrEncryptFail, ex);
        }
    }


    private static void Decrypt(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        Span<byte> plaintextDestination,
        ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != Constants.AesKeySize) throw new ArgumentException(ErrInvalidKeyLength, nameof(key));
        if (nonce.Length != Constants.AesGcmNonceSize)
            throw new ArgumentException(ErrInvalidNonceLength, nameof(nonce));
        if (tag.Length != Constants.AesGcmTagSize) throw new ArgumentException(ErrInvalidTagLength, nameof(tag));
        if (plaintextDestination.Length < ciphertext.Length)
            throw new ArgumentException(ErrBufferTooSmall, nameof(plaintextDestination));
        if (plaintextDestination.Overlaps(ciphertext))
            throw new ArgumentException(ErrBufferOverlap, nameof(plaintextDestination));

        try
        {
            using AesGcm aesGcm = new(key, Constants.AesGcmTagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextDestination, associatedData);
        }
        catch (AuthenticationTagMismatchException authEx)
        {
            throw new ProtocolChainStepException(ErrDecryptFail, authEx);
        }
        catch (CryptographicException cryptoEx)
        {
            throw new ProtocolChainStepException(ErrDecryptFail, cryptoEx);
        }
        catch (Exception ex)
        {
            throw new ProtocolChainStepException(ErrDecryptFail, ex);
        }
    }

    public static (byte[] Ciphertext, byte[] Tag) EncryptAllocating(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[Constants.AesGcmTagSize];
        Encrypt(key, nonce, plaintext, ciphertext, tag, associatedData);
        return (ciphertext, tag);
    }

    public static byte[] DecryptAllocating(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
    {
        byte[] plaintext = new byte[ciphertext.Length];
        Decrypt(key, nonce, ciphertext, tag, plaintext, associatedData);
        return plaintext;
    }
}