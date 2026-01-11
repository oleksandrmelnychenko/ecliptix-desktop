using System;
using System.Globalization;
using System.Text;
using Ecliptix.Protected.Protocol.Sodium;
using Ecliptix.Protected.Protocol.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Feature.Authentication.Services.Authentication;

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

        Exception? actionError = null;
        Result<Unit, SodiumFailure> readResult = _secureHandle.WithReadAccess(span =>
        {
            try
            {
                action(span);
            }
            catch (Exception ex)
            {
                actionError = ex;
            }

            return Result<Unit, SodiumFailure>.Ok(Unit.Value);
        });

        if (actionError != null)
        {
            throw actionError;
        }

        readResult.Unwrap();
    }

    private void ModifyState(int charIndex, int removeCharCount, string insertChars)
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(SecureTextBuffer));
        }

        SodiumSecureMemoryHandle? newHandle = null;
        bool success = false;
        SecureStringHandler? insertHandler = null;
        int insertByteLength = 0;

        try
        {
            if (!TryPrepareInsertHandler(insertChars, out insertHandler, out insertByteLength))
            {
                return;
            }

            int oldByteLength = _secureHandle.Length;
            int currentTextElementCount = GetCurrentTextElementCount(oldByteLength);

            charIndex = Math.Clamp(charIndex, 0, currentTextElementCount);
            removeCharCount = Math.Clamp(removeCharCount, 0, currentTextElementCount - charIndex);

            ByteIndexRange byteRange = CalculateByteIndices(oldByteLength, charIndex, removeCharCount);
            int removedByteCount = byteRange.End - byteRange.Start;
            int newByteLength = oldByteLength - removedByteCount + insertByteLength;

            newHandle = AssembleNewBuffer(oldByteLength, byteRange.Start, byteRange.End, insertHandler, insertByteLength, newByteLength);

            UpdateHandleAndLength(newHandle, currentTextElementCount, removeCharCount, insertChars);
            success = true;
        }
        finally
        {
            insertHandler?.Dispose();
            if (!success)
            {
                newHandle?.Dispose();
            }
        }
    }

    private static bool TryPrepareInsertHandler(
        string insertChars,
        out SecureStringHandler? insertHandler,
        out int insertByteLength)
    {
        insertHandler = null;
        insertByteLength = 0;

        if (string.IsNullOrEmpty(insertChars))
        {
            return true;
        }

        Result<SecureStringHandler, SodiumFailure> handlerResult = SecureStringHandler.FromString(insertChars);
        if (handlerResult.IsErr)
        {
            return false;
        }

        insertHandler = handlerResult.Unwrap();
        insertByteLength = insertHandler.ByteLength;
        return true;
    }

    private int GetCurrentTextElementCount(int byteLength)
    {
        if (byteLength == 0)
        {
            return 0;
        }

        return _secureHandle.WithReadAccess(span =>
        {
            string currentText = Encoding.UTF8.GetString(span[..byteLength]);
            return Result<int, SodiumFailure>.Ok(GetTextElementCount(currentText));
        }).Unwrap();
    }

    private readonly record struct ByteIndexRange(int Start, int End);

    private ByteIndexRange CalculateByteIndices(int oldByteLength, int charIndex, int removeCharCount)
    {
        if (oldByteLength == 0 || (charIndex == 0 && removeCharCount == 0))
        {
            return new ByteIndexRange(0, oldByteLength);
        }

        return _secureHandle.WithReadAccess(span =>
        {
            string currentText = Encoding.UTF8.GetString(span[..oldByteLength]);
            int startByteIndex = GetByteIndexFromTextElementIndex(currentText, charIndex);
            int endByteIndex = GetByteIndexFromTextElementIndex(currentText, charIndex + removeCharCount);
            return Result<ByteIndexRange, SodiumFailure>.Ok(new ByteIndexRange(startByteIndex, endByteIndex));
        }).Unwrap();
    }

    private SodiumSecureMemoryHandle AssembleNewBuffer(
        int oldByteLength,
        int startByteIndex,
        int endByteIndex,
        SecureStringHandler? insertHandler,
        int insertByteLength,
        int newByteLength)
    {
        if (newByteLength == 0)
        {
            return SodiumSecureMemoryHandle.Allocate(0).Unwrap();
        }

        SodiumSecureMemoryHandle newHandle = SodiumSecureMemoryHandle.Allocate(newByteLength).Unwrap();

        if (oldByteLength > 0)
        {
            byte[]? oldData = null;
            _secureHandle.WithReadAccess(oldSpan =>
            {
                oldData = oldSpan.ToArray();
                return Result<Unit, SodiumFailure>.Ok(Unit.Value);
            }).Unwrap();

            newHandle.WithWriteAccess(newSpan =>
            {
                oldData.AsSpan()[..startByteIndex].CopyTo(newSpan);
                oldData.AsSpan()[endByteIndex..].CopyTo(newSpan[(startByteIndex + insertByteLength)..]);
                return Result<Unit, SodiumFailure>.Ok(Unit.Value);
            }).Unwrap();

            if (oldData != null)
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(oldData);
            }

            if (insertByteLength > 0 && insertHandler != null)
            {
                CopyInsertBytesToHandle(insertHandler, newHandle, startByteIndex);
            }
        }
        else if (insertByteLength > 0 && insertHandler != null)
        {
            CopyInsertBytesToHandle(insertHandler, newHandle, 0);
        }

        return newHandle;
    }

    private static void CopyInsertBytesToHandle(SecureStringHandler insertHandler, SodiumSecureMemoryHandle targetHandle, int offset)
    {
        byte[]? insertData = null;
        insertHandler.UseBytes(insertSpan =>
        {
            insertData = insertSpan.ToArray();
            return Unit.Value;
        });

        if (insertData != null)
        {
            targetHandle.WithWriteAccess(targetSpan =>
            {
                insertData.AsSpan().CopyTo(targetSpan[offset..]);
                return Result<Unit, SodiumFailure>.Ok(Unit.Value);
            }).Unwrap();

            System.Security.Cryptography.CryptographicOperations.ZeroMemory(insertData);
        }
    }

    private void UpdateHandleAndLength(
        SodiumSecureMemoryHandle newHandle,
        int currentTextElementCount,
        int removeCharCount,
        string insertChars)
    {
        _secureHandle.Dispose();
        _secureHandle = newHandle;
        Length = currentTextElementCount - removeCharCount + GetTextElementCount(insertChars);
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
