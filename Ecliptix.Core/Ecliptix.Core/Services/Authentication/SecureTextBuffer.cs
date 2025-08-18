using System;
using System.Text;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Protocol.System.Utilities;

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

        try
        {
            int oldByteLength = _secureHandle.Length;
            string oldString = string.Empty;

            if (oldByteLength > 0)
            {
                using var oldBytes = SecureArrayPool.Rent<byte>(oldByteLength);
                _secureHandle.Read(oldBytes.AsSpan()).Unwrap();
                oldString = Encoding.UTF8.GetString(oldBytes.AsSpan());
            }

            charIndex = Math.Clamp(charIndex, 0, Length);
            removeCharCount = Math.Clamp(removeCharCount, 0, Length - charIndex);

            byte[] insertBytes = Encoding.UTF8.GetBytes(insertChars);

            int startByte = oldString.Length > 0 ? Encoding.UTF8.GetByteCount(oldString[..charIndex]) : 0;
            int endByte = oldString.Length > 0
                ? Encoding.UTF8.GetByteCount(oldString[..(charIndex + removeCharCount)])
                : 0;

            int newByteLength = oldByteLength - (endByte - startByte) + insertBytes.Length;

            if (newByteLength > 0)
            {
                using var newBytes = SecureArrayPool.Rent<byte>(newByteLength);
                var newSpan = newBytes.AsSpan();

                if (oldByteLength > 0)
                {
                    using var oldBytesForCopy = SecureArrayPool.Rent<byte>(oldByteLength);
                    _secureHandle.Read(oldBytesForCopy.AsSpan()).Unwrap();
                    var oldSpan = oldBytesForCopy.AsSpan();

                    oldSpan[..startByte].CopyTo(newSpan);
                    insertBytes.CopyTo(newSpan[startByte..]);
                    oldSpan[endByte..].CopyTo(newSpan[(startByte + insertBytes.Length)..]);
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
            Length = Length - removeCharCount + insertChars.Length;
            success = true;
        }
        finally
        {
            if (!success) newHandle?.Dispose();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _secureHandle?.Dispose();
        _isDisposed = true;
    }
}