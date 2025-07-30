// In Ecliptix.Core/Services/Membership/SecureTextBuffer.cs

using System;
using System.Buffers;
using System.Text;
using Ecliptix.Protocol.System.Sodium;

namespace Ecliptix.Core.Services;

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

        byte[]? rentedBytes = null;
        try
        {
            rentedBytes = ArrayPool<byte>.Shared.Rent(_secureHandle.Length);
            Span<byte> span = rentedBytes.AsSpan(0, _secureHandle.Length);

            _secureHandle.Read(span).Unwrap();

            action(span);
        }
        finally
        {
            if (rentedBytes != null)
            {
                rentedBytes.AsSpan(0, _secureHandle.Length).Clear();
                ArrayPool<byte>.Shared.Return(rentedBytes);
            }
        }
    }

    private void ModifyState(int charIndex, int removeCharCount, string insertChars)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SecureTextBuffer));

        byte[]? oldBytes = null;
        byte[]? newBytes = null;
        SodiumSecureMemoryHandle? newHandle = null;
        bool success = false;

        try
        {
            int oldByteLength = _secureHandle.Length;
            string oldString = string.Empty;
            if (oldByteLength > 0)
            {
                oldBytes = ArrayPool<byte>.Shared.Rent(oldByteLength);
                _secureHandle.Read(oldBytes.AsSpan(0, oldByteLength)).Unwrap();
                oldString = Encoding.UTF8.GetString(oldBytes.AsSpan(0, oldByteLength));
            }

            charIndex = Math.Clamp(charIndex, 0, Length);
            removeCharCount = Math.Clamp(removeCharCount, 0, Length - charIndex);

            Span<byte> oldSpan = oldBytes.AsSpan(0, oldByteLength);
            byte[] insertBytes = Encoding.UTF8.GetBytes(insertChars);

            int startByte = oldString.Length > 0 ? Encoding.UTF8.GetByteCount(oldString[..charIndex]) : 0;
            int endByte = oldString.Length > 0
                ? Encoding.UTF8.GetByteCount(oldString[..(charIndex + removeCharCount)])
                : 0;

            int newByteLength = oldByteLength - (endByte - startByte) + insertBytes.Length;
            newBytes = ArrayPool<byte>.Shared.Rent(newByteLength);
            Span<byte> newSpan = newBytes.AsSpan(0, newByteLength);

            oldSpan[..startByte].CopyTo(newSpan);
            insertBytes.CopyTo(newSpan[startByte..]);
            oldSpan[endByte..].CopyTo(newSpan[(startByte + insertBytes.Length)..]);

            newHandle = SodiumSecureMemoryHandle.Allocate(newByteLength).Unwrap();
            if (newByteLength > 0)
            {
                newHandle.Write(newSpan).Unwrap();
            }

            _secureHandle.Dispose();
            _secureHandle = newHandle;
            Length = Length - removeCharCount + insertChars.Length;
            success = true;
        }
        finally
        {
            if (oldBytes != null) ArrayPool<byte>.Shared.Return(oldBytes, true);
            if (newBytes != null) ArrayPool<byte>.Shared.Return(newBytes, true);
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