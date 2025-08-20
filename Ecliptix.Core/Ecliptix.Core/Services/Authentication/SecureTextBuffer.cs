using System;
using System.Security.Cryptography;
using System.Text;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Core.Services.Authentication;

public sealed class SecureTextBuffer : IDisposable
{
    private SodiumSecureMemoryHandle _secureHandle = SodiumSecureMemoryHandle.Allocate(0).Unwrap();
    private bool _isDisposed;

    public int Length { get; private set; }

    public void Insert(int index, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        ModifyState(index, 0, text);
    }

    public void Remove(int index, int count)
    {
        if (count <= 0) return;
        ModifyState(index, count, string.Empty);
    }

    public void WithSecureBytes(Action<ReadOnlySpan<byte>> action)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecureTextBuffer));
        if (_secureHandle.IsInvalid || _secureHandle.Length == 0)
        {
            action(ReadOnlySpan<byte>.Empty);
            return;
        }

        using var rentedBytes = SecureArrayPool.Rent<byte>(_secureHandle.Length);
        Span<byte> span = rentedBytes.AsSpan();

        _secureHandle.Read(span).Unwrap();

        action(span);
    }

    private void ModifyState(int charIndex, int removeCharCount, string insertChars)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecureTextBuffer));

        SodiumSecureMemoryHandle? newHandle = null;
        bool success = false;
        byte[]? insertBytes = null;

        try
        {
            // Use SecureStringHandler.FromString for secure string-to-bytes conversion
            if (string.IsNullOrEmpty(insertChars))
            {
                insertBytes = Array.Empty<byte>();
            }
            else
            {
                // We still need the bytes for the modification operation
                // But we'll minimize exposure by using SecureStringHandler
                Result<SecureStringHandler, SodiumFailure> handlerResult = SecureStringHandler.FromString(insertChars);
                if (handlerResult.IsErr)
                {
                    return; // Failed to create secure handler
                }

                using SecureStringHandler insertHandler = handlerResult.Unwrap();

                // Create a minimal byte array for the insertion
                using var tempBytes = SecureArrayPool.Rent<byte>(insertHandler.ByteLength);
                Result<Unit, SodiumFailure> readResult = insertHandler.UseBytes(bytes =>
                {
                    bytes.CopyTo(tempBytes.AsSpan());
                    return Unit.Value;
                });

                if (readResult.IsErr)
                {
                    return; // Failed to read bytes
                }

                insertBytes = new byte[insertHandler.ByteLength];
                tempBytes.AsSpan()[..insertHandler.ByteLength].CopyTo(insertBytes);
            }

            int oldByteLength = _secureHandle.Length;

            // Calculate positions without creating strings
            int currentCharCount = 0;
            if (oldByteLength > 0)
            {
                using var oldBytes = SecureArrayPool.Rent<byte>(oldByteLength);
                _secureHandle.Read(oldBytes.AsSpan()).Unwrap();
                currentCharCount = Encoding.UTF8.GetCharCount(oldBytes.AsSpan());
            }

            charIndex = Math.Clamp(charIndex, 0, currentCharCount);
            removeCharCount = Math.Clamp(removeCharCount, 0, currentCharCount - charIndex);

            // Calculate byte positions by decoding character by character
            int startByteIndex = 0;
            int endByteIndex = oldByteLength;

            if (oldByteLength > 0 && (charIndex > 0 || removeCharCount > 0))
            {
                using var oldBytes = SecureArrayPool.Rent<byte>(oldByteLength);
                _secureHandle.Read(oldBytes.AsSpan()).Unwrap();

                startByteIndex = GetByteIndexFromCharIndex(oldBytes.AsSpan(), charIndex);
                endByteIndex = GetByteIndexFromCharIndex(oldBytes.AsSpan(), charIndex + removeCharCount);
            }

            int removedByteCount = endByteIndex - startByteIndex;
            int newByteLength = oldByteLength - removedByteCount + insertBytes.Length;

            if (newByteLength > 0)
            {
                using var newBytes = SecureArrayPool.Rent<byte>(newByteLength);
                var newSpan = newBytes.AsSpan();

                if (oldByteLength > 0)
                {
                    using var oldBytesForCopy = SecureArrayPool.Rent<byte>(oldByteLength);
                    _secureHandle.Read(oldBytesForCopy.AsSpan()).Unwrap();
                    var oldSpan = oldBytesForCopy.AsSpan();

                    // Copy before
                    oldSpan[..startByteIndex].CopyTo(newSpan);

                    // Copy insert
                    insertBytes.CopyTo(newSpan[startByteIndex..]);

                    // Copy after
                    oldSpan[endByteIndex..].CopyTo(newSpan[(startByteIndex + insertBytes.Length)..]);
                }
                else
                {
                    insertBytes.CopyTo(newSpan);
                }

                newHandle = SodiumSecureMemoryHandle.Allocate(newByteLength).Unwrap();
                newHandle.Write(newSpan).Unwrap();
            }
            else
            {
                newHandle = SodiumSecureMemoryHandle.Allocate(0).Unwrap();
            }

            _secureHandle.Dispose();
            _secureHandle = newHandle;
            Length = currentCharCount - removeCharCount + Encoding.UTF8.GetCharCount(insertBytes);
            success = true;
        }
        finally
        {
            if (insertBytes != null && insertBytes != Array.Empty<byte>())
            {
                CryptographicOperations.ZeroMemory(insertBytes);
            }
            if (!success)
            {
                newHandle?.Dispose();
            }
        }
    }

    private static int GetByteIndexFromCharIndex(ReadOnlySpan<byte> bytes, int charIndex)
    {
        if (charIndex == 0) return 0;

        int currentCharIndex = 0;
        int byteIndex = 0;

        while (byteIndex < bytes.Length && currentCharIndex < charIndex)
        {
            // Skip UTF-8 continuation bytes (0x80-0xBF)
            if ((bytes[byteIndex] & 0xC0) != 0x80)
            {
                currentCharIndex++;
            }
            byteIndex++;
        }

        return byteIndex;
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _secureHandle?.Dispose();
        _isDisposed = true;
    }
}