using System;
using System.Globalization;
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
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ModifyState(index, 0, text);
    }

    public void Remove(int index, int count)
    {
        if (count <= 0)
        {
            return;
        }

        ModifyState(index, count, string.Empty);
    }

    public void WithSecureBytes(Action<ReadOnlySpan<byte>> action)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SecureTextBuffer));
        }

        if (_secureHandle.IsInvalid || _secureHandle.Length == 0)
        {
            action(ReadOnlySpan<byte>.Empty);
            return;
        }

        using SecurePooledArray<byte> rentedBytes = SecureArrayPool.Rent<byte>(_secureHandle.Length);
        Span<byte> span = rentedBytes.AsSpan();

        _secureHandle.Read(span).Unwrap();

        action(span);
    }

    private void ModifyState(int charIndex, int removeCharCount, string insertChars)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SecureTextBuffer));
        }

        SodiumSecureMemoryHandle? newHandle = null;
        bool success = false;
        byte[]? insertBytes = null;

        try
        {
            if (string.IsNullOrEmpty(insertChars))
            {
                insertBytes = [];
            }
            else
            {
                Result<SecureStringHandler, SodiumFailure> handlerResult = SecureStringHandler.FromString(insertChars);
                if (handlerResult.IsErr)
                {
                    return;
                }

                using SecureStringHandler insertHandler = handlerResult.Unwrap();

                SecurePooledArray<byte> tempBytes = SecureArrayPool.Rent<byte>(insertHandler.ByteLength);
                try
                {
                    Result<Unit, SodiumFailure> readResult = insertHandler.UseBytes(bytes =>
                    {
                        bytes.CopyTo(tempBytes.AsSpan());
                        return Unit.Value;
                    });

                    if (readResult.IsErr)
                    {
                        return;
                    }

                    insertBytes = new byte[insertHandler.ByteLength];
                    tempBytes.AsSpan()[..insertHandler.ByteLength].CopyTo(insertBytes);
                }
                finally
                {
                    tempBytes.Dispose();
                }
            }

            int oldByteLength = _secureHandle.Length;

            int currentTextElementCount = 0;
            if (oldByteLength > 0)
            {
                using SecurePooledArray<byte> oldBytes = SecureArrayPool.Rent<byte>(oldByteLength);
                _secureHandle.Read(oldBytes.AsSpan()).Unwrap();
                string currentText = Encoding.UTF8.GetString(oldBytes.AsSpan());
                currentTextElementCount = GetTextElementCount(currentText);
            }

            charIndex = Math.Clamp(charIndex, 0, currentTextElementCount);
            removeCharCount = Math.Clamp(removeCharCount, 0, currentTextElementCount - charIndex);

            int startByteIndex = 0;
            int endByteIndex = oldByteLength;

            if (oldByteLength > 0 && (charIndex > 0 || removeCharCount > 0))
            {
                using SecurePooledArray<byte> oldBytes = SecureArrayPool.Rent<byte>(oldByteLength);
                _secureHandle.Read(oldBytes.AsSpan()).Unwrap();
                string currentText = Encoding.UTF8.GetString(oldBytes.AsSpan());

                startByteIndex = GetByteIndexFromTextElementIndex(currentText, charIndex);
                endByteIndex = GetByteIndexFromTextElementIndex(currentText, charIndex + removeCharCount);
            }

            int removedByteCount = endByteIndex - startByteIndex;
            int newByteLength = oldByteLength - removedByteCount + insertBytes.Length;

            if (newByteLength > 0)
            {
                using SecurePooledArray<byte> newBytes = SecureArrayPool.Rent<byte>(newByteLength);
                Span<byte> newSpan = newBytes.AsSpan();

                if (oldByteLength > 0)
                {
                    using SecurePooledArray<byte> oldBytesForCopy = SecureArrayPool.Rent<byte>(oldByteLength);
                    _secureHandle.Read(oldBytesForCopy.AsSpan()).Unwrap();
                    Span<byte> oldSpan = oldBytesForCopy.AsSpan();

                    oldSpan[..startByteIndex].CopyTo(newSpan);

                    insertBytes.CopyTo(newSpan[startByteIndex..]);

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
            string insertText = insertBytes.Length > 0 ? Encoding.UTF8.GetString(insertBytes) : string.Empty;
            Length = currentTextElementCount - removeCharCount + GetTextElementCount(insertText);
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

    private static int GetByteIndexFromTextElementIndex(string text, int textElementIndex)
    {
        if (textElementIndex == 0 || string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            StringInfo stringInfo = new(text);
            int textElementCount = stringInfo.LengthInTextElements;

            if (textElementIndex >= textElementCount)
            {
                return Encoding.UTF8.GetByteCount(text);
            }

            string substring = stringInfo.SubstringByTextElements(0, textElementIndex);
            return Encoding.UTF8.GetByteCount(substring);
        }
        catch (Exception)
        {
            return Math.Min(textElementIndex, Encoding.UTF8.GetByteCount(text));
        }
    }

    private static int GetTextElementCount(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            StringInfo stringInfo = new(text);
            return stringInfo.LengthInTextElements;
        }
        catch (Exception)
        {
            return text.Length;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _secureHandle?.Dispose();
        _isDisposed = true;
    }
}
