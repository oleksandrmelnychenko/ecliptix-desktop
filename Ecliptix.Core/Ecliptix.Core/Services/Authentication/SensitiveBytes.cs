using System;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Core.Services.Authentication;

internal sealed class SensitiveBytes : IDisposable
{
    private SodiumSecureMemoryHandle? _handle;
    private int _length;
    private bool _disposed;

    private SensitiveBytes(SodiumSecureMemoryHandle handle, int length)
    {
        _handle = handle;
        _length = length;
    }

    public int Length
    {
        get
        {
            EnsureNotDisposed();
            return _length;
        }
    }

    public static Result<SensitiveBytes, SodiumFailure> From(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
        {
            Result<SodiumSecureMemoryHandle, SodiumFailure> emptyResult =
                SodiumSecureMemoryHandle.Allocate(0);

            if (emptyResult.IsErr)
                return Result<SensitiveBytes, SodiumFailure>.Err(emptyResult.UnwrapErr());

            return Result<SensitiveBytes, SodiumFailure>.Ok(
                new SensitiveBytes(emptyResult.Unwrap(), 0));
        }

        Result<SodiumSecureMemoryHandle, SodiumFailure> allocResult =
            SodiumSecureMemoryHandle.Allocate(source.Length);

        if (allocResult.IsErr)
            return Result<SensitiveBytes, SodiumFailure>.Err(allocResult.UnwrapErr());

        SodiumSecureMemoryHandle handle = allocResult.Unwrap();

        Result<Unit, SodiumFailure> writeResult = handle.Write(source);
        if (writeResult.IsErr)
        {
            handle.Dispose();
            return Result<SensitiveBytes, SodiumFailure>.Err(writeResult.UnwrapErr());
        }

        return Result<SensitiveBytes, SodiumFailure>.Ok(
            new SensitiveBytes(handle, source.Length));
    }

    public Result<TResult, SodiumFailure> WithReadAccess<TResult>(
        Func<ReadOnlySpan<byte>, Result<TResult, SodiumFailure>> operation)
    {
        EnsureNotDisposed();

        if (_handle == null)
            return Result<TResult, SodiumFailure>.Err(
                SodiumFailure.NullPointer("SensitiveBytes handle is null"));

        return _handle.WithReadAccess(operation);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _handle?.Dispose();
        _handle = null;
        _length = 0;
        _disposed = true;
    }

    private void EnsureNotDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SensitiveBytes));
    }
}
