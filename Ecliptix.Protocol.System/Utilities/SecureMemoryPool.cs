using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Protocol.System.Utilities;

public sealed class SecureMemoryPool : IDisposable
{
    private readonly ConcurrentBag<SecureMemoryBuffer> _pool = new();
    private readonly int _defaultBufferSize;
    private readonly int _maxPoolSize;
    private int _currentPoolSize;
    private bool _disposed;

    public SecureMemoryPool(int defaultBufferSize = 4096, int maxPoolSize = 100)
    {
        if (defaultBufferSize <= 0)
            throw new ArgumentException("Buffer size must be positive", nameof(defaultBufferSize));
        if (maxPoolSize <= 0)
            throw new ArgumentException("Max pool size must be positive", nameof(maxPoolSize));

        _defaultBufferSize = defaultBufferSize;
        _maxPoolSize = maxPoolSize;
    }

    public SecureMemoryBuffer Rent(int minimumSize = -1)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SecureMemoryPool));

        int requestedSize = minimumSize > 0 ? minimumSize : _defaultBufferSize;
        int allocatedSize = minimumSize > 0 ? Math.Max(minimumSize, _defaultBufferSize) : _defaultBufferSize;

        while (_pool.TryTake(out SecureMemoryBuffer? buffer))
        {
            if (!buffer.IsDisposed && buffer.AllocatedSize >= requestedSize)
            {
                buffer.Clear();
                buffer.SetRequestedSize(requestedSize);
                return buffer;
            }

            buffer.Dispose();
            Interlocked.Decrement(ref _currentPoolSize);
        }

        return new SecureMemoryBuffer(requestedSize, allocatedSize, this);
    }

    internal void Return(SecureMemoryBuffer buffer)
    {
        if (_disposed || buffer.IsDisposed)
        {
            buffer.Dispose();
            return;
        }

        buffer.Clear();

        if (_currentPoolSize < _maxPoolSize)
        {
            _pool.Add(buffer);
            Interlocked.Increment(ref _currentPoolSize);
        }
        else
        {
            buffer.Dispose();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        while (_pool.TryTake(out SecureMemoryBuffer? buffer))
        {
            buffer.Dispose();
        }
    }
}

public sealed class SecureMemoryBuffer : IDisposable
{
    private readonly SecureMemoryPool? _pool;
    private readonly SodiumSecureMemoryHandle _handle;
    private bool _disposed;
    private int _requestedSize;
    private readonly int _allocatedSize;

    public int Length => _requestedSize;
    public int AllocatedSize => _allocatedSize;
    public bool IsDisposed => _disposed;

    internal SecureMemoryBuffer(int requestedSize, int allocatedSize, SecureMemoryPool? pool = null)
    {
        _pool = pool;
        _requestedSize = requestedSize;
        _allocatedSize = allocatedSize;

        Result<SodiumSecureMemoryHandle, SodiumFailure> result = SodiumSecureMemoryHandle.Allocate(allocatedSize);
        if (result.IsErr)
            throw new InvalidOperationException($"Failed to allocate secure memory: {result.UnwrapErr()}");

        _handle = result.Unwrap();
    }

    internal SecureMemoryBuffer(int size, SecureMemoryPool? pool = null)
        : this(size, size, pool)
    {
    }

    internal void SetRequestedSize(int requestedSize)
    {
        if (requestedSize > _allocatedSize)
            throw new ArgumentException($"Requested size {requestedSize} exceeds allocated size {_allocatedSize}");
        _requestedSize = requestedSize;
    }

    public Span<byte> GetSpan()
    {
        if (!_disposed)
        {
            byte[] tempBuffer = new byte[AllocatedSize];
            Result<Unit, SodiumFailure> readResult = _handle.Read(tempBuffer);
            return readResult.IsErr
                ? throw new InvalidOperationException($"Failed to read secure memory: {readResult.UnwrapErr()}")
                : tempBuffer.AsSpan(0, Length);
        }

        throw new ObjectDisposedException(nameof(SecureMemoryBuffer));
    }

    public Result<Unit, SodiumFailure> Write(ReadOnlySpan<byte> data)
    {
        if (_disposed)
            return Result<Unit, SodiumFailure>.Err(
                SodiumFailure.NullPointer("Buffer is disposed"));

        return _handle.Write(data);
    }

    public Result<Unit, SodiumFailure> Read(Span<byte> destination)
    {
        if (_disposed)
            return Result<Unit, SodiumFailure>.Err(
                SodiumFailure.NullPointer("Buffer is disposed"));

        return _handle.Read(destination);
    }

    internal void Clear()
    {
        if (_disposed) return;

        Span<byte> zeros = stackalloc byte[Math.Min(AllocatedSize, 1024)];
        zeros.Clear();

        for (int offset = 0; offset < AllocatedSize; offset += zeros.Length)
        {
            int chunkSize = Math.Min(zeros.Length, AllocatedSize - offset);
            _ = _handle.Write(zeros[..chunkSize]);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        Clear();

        if (_pool != null)
        {
            _pool.Return(this);
        }
        else
        {
            _handle?.Dispose();
        }
    }
}

public static class SecureMemoryUtils
{
    private static readonly SecureMemoryPool DefaultPool = new(4096, 100);

    public static Result<TResult, TError> WithSecureBuffer<TResult, TError>(
        int size,
        Func<Span<byte>, Result<TResult, TError>> operation)
        where TError : class
    {
        using SecureMemoryBuffer buffer = DefaultPool.Rent(size);

        byte[] fullBuffer = new byte[buffer.AllocatedSize];
        Result<Unit, SodiumFailure> readResult = buffer.Read(fullBuffer);
        if (readResult.IsErr)
            throw new InvalidOperationException($"Failed to read secure memory: {readResult.UnwrapErr()}");

        Span<byte> span = fullBuffer.AsSpan(0, size);

        try
        {
            return operation(span);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fullBuffer);
        }
    }

    public static Result<TResult, TError> WithSecureBuffers<TResult, TError>(
        int[] sizes,
        Func<SecureMemoryBuffer[], Result<TResult, TError>> operation)
        where TError : class
    {
        SecureMemoryBuffer[] buffers = new SecureMemoryBuffer[sizes.Length];

        try
        {
            for (int i = 0; i < sizes.Length; i++)
            {
                buffers[i] = DefaultPool.Rent(sizes[i]);
            }

            return operation(buffers);
        }
        finally
        {
            foreach (SecureMemoryBuffer buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}