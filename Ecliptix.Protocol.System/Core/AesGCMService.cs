using System.Security.Cryptography;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Sodium;

// Required for AesGcm

// For Constants, ShieldChainStepException

namespace Ecliptix.Protocol.System.Core;
// Or your preferred namespace

/// <summary>
///     Provides AES-256-GCM authenticated encryption and decryption services using the built-in .NET API.
///     WARNING: Nonce uniqueness per key is CRITICAL for AES-GCM security.
/// </summary>
public static class AesGcmService
{
    private const string ErrInvalidKeyLength = "Invalid AES key length";
    private const string ErrInvalidNonceLength = "Invalid AES-GCM nonce length";
    private const string ErrInvalidTagLength = "Invalid AES-GCM tag length";
    private const string ErrEncryptFail = "AES-GCM encryption failed";
    private const string ErrDecryptFail = "AES-GCM decryption failed (authentication tag mismatch)";
    private const string ErrBufferTooSmall = "Destination buffer is too small";


    /// <summary>
    ///     Encrypts plaintext using AES-256-GCM.
    /// </summary>
    /// <param name="key">The 32-byte AES key.</param>
    /// <param name="nonce">The 12-byte nonce (MUST be unique per key).</param>
    /// <param name="plaintext">The data to encrypt.</param>
    /// <param name="ciphertextDestination">The buffer to write the ciphertext to. Must be >= plaintext.Length.</param>
    /// <param name="tagDestination">The buffer to write the 16-byte authentication tag to.</param>
    /// <param name="associatedData">Optional associated data (can be ReadOnlySpan<byte>.Empty).</param>
    /// <exception cref="ArgumentNullException">
    ///     Thrown if key, nonce, plaintext, ciphertextDestination, or tagDestination is
    ///     implicitly null via default span.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown for invalid key/nonce/tag lengths or if ciphertextDestination is too small.</exception>
    /// <exception cref="CryptographicException">Thrown for other cryptographic errors during encryption.</exception>
    /// <exception cref="ProtocolChainStepException">Wrapped cryptographic exceptions.</exception>
    public static void Encrypt(
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

    /// <summary>
    ///     Decrypts ciphertext using AES-256-GCM and verifies the authentication tag.
    /// </summary>
    /// <param name="key">The 32-byte AES key.</param>
    /// <param name="nonce">The 12-byte nonce used during encryption.</param>
    /// <param name="ciphertext">The encrypted data.</param>
    /// <param name="tag">The 16-byte authentication tag.</param>
    /// <param name="plaintextDestination">The buffer to write the decrypted plaintext to. Must be >= ciphertext.Length.</param>
    /// <param name="associatedData">Optional associated data (must match encryption AD).</param>
    /// <exception cref="ArgumentNullException">...</exception>
    /// <exception cref="ArgumentException">Thrown for invalid key/nonce/tag lengths or if plaintextDestination is too small.</exception>
    /// <exception cref="AuthenticationTagMismatchException">(Subclass of CryptographicException) Thrown if the tag is invalid.</exception>
    /// <exception cref="CryptographicException">Thrown for other cryptographic errors during decryption.</exception>
    /// <exception cref="ProtocolChainStepException">Wrapped cryptographic exceptions.</exception>
    public static void Decrypt(
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


    // --- Convenience methods returning byte[] (less performant due to allocations) ---

    /// <summary>
    ///     Encrypts plaintext using AES-256-GCM with secure memory management.
    ///     Uses secure buffers that are automatically wiped after use.
    /// </summary>
    /// <returns>Result containing the operation with secure ciphertext and tag buffers.</returns>
    public static Result<TResult, ProtocolChainStepException> EncryptWithSecureMemory<TResult>(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData,
        Func<ReadOnlySpan<byte>, ReadOnlySpan<byte>, TResult> operation)
    {
        if (key.Length != Constants.AesKeySize)
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrInvalidKeyLength));
        if (nonce.Length != Constants.AesGcmNonceSize)
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrInvalidNonceLength));

        // Convert spans to arrays for use in lambda
        var keyArray = key.ToArray();
        var nonceArray = nonce.ToArray();
        var plaintextArray = plaintext.ToArray();
        var associatedDataArray = associatedData.ToArray();
        var plaintextLength = plaintext.Length;

        try
        {
            return SecureMemoryUtils.WithSecureBuffers(
                new[] { plaintextLength, Constants.AesGcmTagSize },
                buffers =>
                {
                    var ciphertextSpan = buffers[0].GetSpan().Slice(0, plaintextLength);
                    var tagSpan = buffers[1].GetSpan().Slice(0, Constants.AesGcmTagSize);

                    using AesGcm aesGcm = new(keyArray, Constants.AesGcmTagSize);
                    aesGcm.Encrypt(nonceArray, plaintextArray, ciphertextSpan, tagSpan, associatedDataArray);

                    return Result<TResult, ProtocolChainStepException>.Ok(
                        operation(ciphertextSpan, tagSpan));
                });
        }
        catch (CryptographicException cryptoEx)
        {
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrEncryptFail, cryptoEx));
        }
        catch (Exception ex)
        {
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrEncryptFail, ex));
        }
        finally
        {
            // Securely wipe the copied arrays
            CryptographicOperations.ZeroMemory(keyArray);
            CryptographicOperations.ZeroMemory(nonceArray);
            CryptographicOperations.ZeroMemory(plaintextArray);
            CryptographicOperations.ZeroMemory(associatedDataArray);
        }
    }

    /// <summary>
    ///     Encrypts plaintext using AES-256-GCM, allocating and returning ciphertext and tag.
    ///     WARNING: Uses regular memory allocation without secure wiping.
    ///     Consider using EncryptWithSecureMemory for sensitive data.
    /// </summary>
    /// <returns>A tuple containing the ciphertext and the tag.</returns>
    [Obsolete("Use EncryptWithSecureMemory for secure handling of sensitive data")]
    public static (byte[] Ciphertext, byte[] Tag) EncryptAllocating(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> associatedData = default)
    {
        using var ciphertextMemory = ScopedSecureMemory.Allocate(plaintext.Length);
        using var tagMemory = ScopedSecureMemory.Allocate(Constants.AesGcmTagSize);

        Encrypt(key, nonce, plaintext, ciphertextMemory.AsSpan(), tagMemory.AsSpan(), associatedData);

        // Note: We have to copy to regular arrays for the return value
        // This breaks the secure memory chain but maintains API compatibility
        return (ciphertextMemory.AsSpan().ToArray(), tagMemory.AsSpan().ToArray());
    }

    /// <summary>
    ///     Decrypts ciphertext using AES-256-GCM with secure memory management.
    ///     Uses secure buffers that are automatically wiped after use.
    /// </summary>
    /// <returns>Result containing the operation with secure plaintext buffer.</returns>
    public static Result<TResult, ProtocolChainStepException> DecryptWithSecureMemory<TResult>(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> associatedData,
        Func<ReadOnlySpan<byte>, TResult> operation)
    {
        if (key.Length != Constants.AesKeySize)
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrInvalidKeyLength));
        if (nonce.Length != Constants.AesGcmNonceSize)
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrInvalidNonceLength));
        if (tag.Length != Constants.AesGcmTagSize)
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrInvalidTagLength));

        // Convert spans to arrays for use in lambda
        var keyArray = key.ToArray();
        var nonceArray = nonce.ToArray();
        var ciphertextArray = ciphertext.ToArray();
        var tagArray = tag.ToArray();
        var associatedDataArray = associatedData.ToArray();
        var ciphertextLength = ciphertext.Length;

        try
        {
            return SecureMemoryUtils.WithSecureBuffer(
                ciphertextLength,
                plaintextSpan =>
                {
                    using AesGcm aesGcm = new(keyArray, Constants.AesGcmTagSize);
                    aesGcm.Decrypt(nonceArray, ciphertextArray, tagArray, plaintextSpan, associatedDataArray);

                    return Result<TResult, ProtocolChainStepException>.Ok(
                        operation(plaintextSpan));
                });
        }
        catch (AuthenticationTagMismatchException authEx)
        {
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrDecryptFail, authEx));
        }
        catch (CryptographicException cryptoEx)
        {
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrDecryptFail, cryptoEx));
        }
        catch (Exception ex)
        {
            return Result<TResult, ProtocolChainStepException>.Err(
                new ProtocolChainStepException(ErrDecryptFail, ex));
        }
        finally
        {
            // Securely wipe the copied arrays
            CryptographicOperations.ZeroMemory(keyArray);
            CryptographicOperations.ZeroMemory(nonceArray);
            CryptographicOperations.ZeroMemory(ciphertextArray);
            CryptographicOperations.ZeroMemory(tagArray);
            CryptographicOperations.ZeroMemory(associatedDataArray);
        }
    }

    /// <summary>
    ///     Decrypts ciphertext using AES-256-GCM, allocating and returning plaintext.
    ///     WARNING: Uses regular memory allocation without secure wiping.
    ///     Consider using DecryptWithSecureMemory for sensitive data.
    /// </summary>
    /// <returns>The decrypted plaintext.</returns>
    [Obsolete("Use DecryptWithSecureMemory for secure handling of sensitive data")]
    public static byte[] DecryptAllocating(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> associatedData = default)
    {
        using var plaintextMemory = ScopedSecureMemory.Allocate(ciphertext.Length);
        Decrypt(key, nonce, ciphertext, tag, plaintextMemory.AsSpan(), associatedData);

        // Note: We have to copy to regular array for the return value
        // This breaks the secure memory chain but maintains API compatibility
        return plaintextMemory.AsSpan().ToArray();
    }
}