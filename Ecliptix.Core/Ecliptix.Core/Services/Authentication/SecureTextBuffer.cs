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
            insertBytes = PrepareInsertBytes(insertChars);
            if (insertBytes == null)
            {
                return;
            }

            int oldByteLength = _secureHandle.Length;
            int currentTextElementCount = GetCurrentTextElementCount(oldByteLength);

            charIndex = Math.Clamp(charIndex, 0, currentTextElementCount);
            removeCharCount = Math.Clamp(removeCharCount, 0, currentTextElementCount - charIndex);

            ByteIndexRange byteRange = CalculateByteIndices(oldByteLength, charIndex, removeCharCount);
            int removedByteCount = byteRange.End - byteRange.Start;
            int newByteLength = oldByteLength - removedByteCount + insertBytes.Length;

            newHandle = AssembleNewBuffer(oldByteLength, byteRange.Start, byteRange.End, insertBytes, newByteLength);

            UpdateHandleAndLength(newHandle, currentTextElementCount, removeCharCount, insertBytes);
            success = true;
        }
        finally
        {
            CleanupInsertBytes(insertBytes);
            if (!success)
            {
                newHandle?.Dispose();
            }
        }
    }

    private static byte[]? PrepareInsertBytes(string insertChars)
    {
        if (string.IsNullOrEmpty(insertChars))
        {
            return [];
        }

        Result<SecureStringHandler, SodiumFailure> handlerResult = SecureStringHandler.FromString(insertChars);
        if (handlerResult.IsErr)
        {
            return null;
        }

        using SecureStringHandler insertHandler = handlerResult.Unwrap();
        using SecurePooledArray<byte> tempBytes = SecureArrayPool.Rent<byte>(insertHandler.ByteLength);

        Result<Unit, SodiumFailure> readResult = insertHandler.UseBytes(bytes =>
        {
            bytes.CopyTo(tempBytes.AsSpan());
            return Unit.Value;
        });

        if (readResult.IsErr)
        {
            return null;
        }

        byte[] insertBytes = new byte[insertHandler.ByteLength];
        tempBytes.AsSpan()[..insertHandler.ByteLength].CopyTo(insertBytes);
        return insertBytes;
    }

    private int GetCurrentTextElementCount(int byteLength)
    {
        if (byteLength == 0)
        {
            return 0;
        }

        using SecurePooledArray<byte> oldBytes = SecureArrayPool.Rent<byte>(byteLength);
        _secureHandle.Read(oldBytes.AsSpan()).Unwrap();
        string currentText = Encoding.UTF8.GetString(oldBytes.AsSpan());
        return GetTextElementCount(currentText);
    }

    private readonly record struct ByteIndexRange(int Start, int End);

    private ByteIndexRange CalculateByteIndices(int oldByteLength, int charIndex, int removeCharCount)
    {
        if (oldByteLength == 0 || (charIndex == 0 && removeCharCount == 0))
        {
            return new ByteIndexRange(0, oldByteLength);
        }

        using SecurePooledArray<byte> oldBytes = SecureArrayPool.Rent<byte>(oldByteLength);
        _secureHandle.Read(oldBytes.AsSpan()).Unwrap();
        string currentText = Encoding.UTF8.GetString(oldBytes.AsSpan());

        int startByteIndex = GetByteIndexFromTextElementIndex(currentText, charIndex);
        int endByteIndex = GetByteIndexFromTextElementIndex(currentText, charIndex + removeCharCount);

        return new ByteIndexRange(startByteIndex, endByteIndex);
    }

    private SodiumSecureMemoryHandle AssembleNewBuffer(int oldByteLength, int startByteIndex, int endByteIndex, byte[] insertBytes, int newByteLength)
    {
        if (newByteLength == 0)
        {
            return SodiumSecureMemoryHandle.Allocate(0).Unwrap();
        }

        using SecurePooledArray<byte> newBytes = SecureArrayPool.Rent<byte>(newByteLength);
        Span<byte> newSpan = newBytes.AsSpan();

        if (oldByteLength > 0)
        {
            CopyBufferSegments(oldByteLength, startByteIndex, endByteIndex, insertBytes, newSpan);
        }
        else
        {
            insertBytes.CopyTo(newSpan);
        }

        SodiumSecureMemoryHandle newHandle = SodiumSecureMemoryHandle.Allocate(newByteLength).Unwrap();
        newHandle.Write(newSpan).Unwrap();
        return newHandle;
    }

    private void CopyBufferSegments(int oldByteLength, int startByteIndex, int endByteIndex, byte[] insertBytes, Span<byte> newSpan)
    {
        using SecurePooledArray<byte> oldBytesForCopy = SecureArrayPool.Rent<byte>(oldByteLength);
        _secureHandle.Read(oldBytesForCopy.AsSpan()).Unwrap();
        Span<byte> oldSpan = oldBytesForCopy.AsSpan();

        oldSpan[..startByteIndex].CopyTo(newSpan);
        insertBytes.CopyTo(newSpan[startByteIndex..]);
        oldSpan[endByteIndex..].CopyTo(newSpan[(startByteIndex + insertBytes.Length)..]);
    }

    private void UpdateHandleAndLength(SodiumSecureMemoryHandle newHandle, int currentTextElementCount, int removeCharCount, byte[] insertBytes)
    {
        _secureHandle.Dispose();
        _secureHandle = newHandle;
        string insertText = insertBytes.Length > 0 ? Encoding.UTF8.GetString(insertBytes) : string.Empty;
        Length = currentTextElementCount - removeCharCount + GetTextElementCount(insertText);
    }

    private static void CleanupInsertBytes(byte[]? insertBytes)
    {
        if (insertBytes != null && insertBytes.Length > 0)
        {
            CryptographicOperations.ZeroMemory(insertBytes);
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[SECURE-TEXT-BUFFER] Failed to calculate byte index for text element. TextElementIndex: {Index}, Fallback used",
                textElementIndex);
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[SECURE-TEXT-BUFFER] Failed to get text element count. TextLength: {Length}, Fallback used",
                text.Length);
            return text.Length;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _secureHandle.Dispose();
        _isDisposed = true;
    }
}
