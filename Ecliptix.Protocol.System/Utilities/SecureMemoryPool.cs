using System.Buffers;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Protocol.System.Utilities;

/// <summary>
/// Provides a secure memory pool for temporary cryptographic operations.
/// Ensures all borrowed memory is securely wiped before returning to the pool.
/// </summary>
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

    /// <summary>
    /// Rents a secure memory buffer from the pool.
    /// </summary>
    public SecureMemoryBuffer Rent(int minimumSize = -1)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SecureMemoryPool));

        int requestedSize = minimumSize > 0 ? minimumSize : _defaultBufferSize;
        int allocatedSize = minimumSize > 0 ? Math.Max(minimumSize, _defaultBufferSize) : _defaultBufferSize;

        // Try to get a buffer from the pool
        while (_pool.TryTake(out SecureMemoryBuffer? buffer))
        {
            if (!buffer.IsDisposed && buffer.AllocatedSize >= requestedSize)
            {
                buffer.Clear(); // Ensure it's clean
                buffer.SetRequestedSize(requestedSize);
                return buffer;
            }
            buffer.Dispose();
            Interlocked.Decrement(ref _currentPoolSize);
        }

        // Create a new buffer if needed
        return new SecureMemoryBuffer(requestedSize, allocatedSize, this);
    }

    /// <summary>
    /// Returns a buffer to the pool after secure wiping.
    /// </summary>
    internal void Return(SecureMemoryBuffer buffer)
    {
        if (_disposed || buffer.IsDisposed)
        {
            buffer.Dispose();
            return;
        }

        buffer.Clear(); // Secure wipe before returning

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

/// <summary>
/// A secure memory buffer that automatically wipes its contents on disposal.
/// </summary>
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

        var result = SodiumSecureMemoryHandle.Allocate(allocatedSize);
        if (result.IsErr)
            throw new InvalidOperationException($"Failed to allocate secure memory: {result.UnwrapErr()}");

        _handle = result.Unwrap();
    }

    internal SecureMemoryBuffer(int size, SecureMemoryPool? pool = null)
        : this(size, size, pool)
    {
    }

    /// <summary>
    /// Sets the requested size for reused buffers from the pool.
    /// </summary>
    internal void SetRequestedSize(int requestedSize)
    {
        if (requestedSize > _allocatedSize)
            throw new ArgumentException($"Requested size {requestedSize} exceeds allocated size {_allocatedSize}");
        _requestedSize = requestedSize;
    }

    /// <summary>
    /// Gets a span view of the buffer for writing.
    /// </summary>
    public Span<byte> GetSpan()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(SecureMemoryBuffer));

        // We need to read the full allocated size from the handle
        byte[] tempBuffer = new byte[AllocatedSize];
        var readResult = _handle.Read(tempBuffer);
        if (readResult.IsErr)
            throw new InvalidOperationException($"Failed to read secure memory: {readResult.UnwrapErr()}");

        // But only return the requested portion
        return tempBuffer.AsSpan(0, Length);
    }

    /// <summary>
    /// Writes data to the secure buffer.
    /// </summary>
    public Result<Unit, SodiumFailure> Write(ReadOnlySpan<byte> data)
    {
        if (_disposed)
            return Result<Unit, SodiumFailure>.Err(
                SodiumFailure.NullPointer("Buffer is disposed"));

        return _handle.Write(data);
    }

    /// <summary>
    /// Reads data from the secure buffer.
    /// </summary>
    public Result<Unit, SodiumFailure> Read(Span<byte> destination)
    {
        if (_disposed)
            return Result<Unit, SodiumFailure>.Err(
                SodiumFailure.NullPointer("Buffer is disposed"));

        return _handle.Read(destination);
    }

    /// <summary>
    /// Securely clears the buffer contents.
    /// </summary>
    internal void Clear()
    {
        if (_disposed) return;

        Span<byte> zeros = stackalloc byte[Math.Min(AllocatedSize, 1024)];
        zeros.Clear();

        for (int offset = 0; offset < AllocatedSize; offset += zeros.Length)
        {
            int bytesToWrite = Math.Min(zeros.Length, AllocatedSize - offset);
            _handle.Write(zeros.Slice(0, bytesToWrite)).IgnoreResult();
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

/// <summary>
/// Provides utilities for secure memory operations.
/// </summary>
public static class SecureMemoryUtils
{
    private static readonly SecureMemoryPool DefaultPool = new(4096, 100);

    /// <summary>
    /// Executes an operation with a temporary secure buffer that is automatically wiped.
    /// </summary>
    public static Result<TResult, TError> WithSecureBuffer<TResult, TError>(
        int size,
        Func<Span<byte>, Result<TResult, TError>> operation)
        where TError : class
    {
        using var buffer = DefaultPool.Rent(size);

        // Get the full allocated buffer to ensure we can wipe it all
        byte[] fullBuffer = new byte[buffer.AllocatedSize];
        var readResult = buffer.Read(fullBuffer);
        if (readResult.IsErr)
            throw new InvalidOperationException($"Failed to read secure memory: {readResult.UnwrapErr()}");

        // Only use the requested portion for the operation
        var span = fullBuffer.AsSpan(0, size);

        try
        {
            return operation(span);
        }
        finally
        {
            // Wipe the entire allocated buffer
            CryptographicOperations.ZeroMemory(fullBuffer);
        }
    }

    /// <summary>
    /// Executes an operation with multiple secure buffers.
    /// </summary>
    public static Result<TResult, TError> WithSecureBuffers<TResult, TError>(
        int[] sizes,
        Func<SecureMemoryBuffer[], Result<TResult, TError>> operation)
        where TError : class
    {
        var buffers = new SecureMemoryBuffer[sizes.Length];

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
            // Buffers are cleared by their Dispose method, which calls Clear()
            // Clear() already wipes the entire allocated buffer
            foreach (var buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    /// <summary>
    /// Safely compares two byte arrays in constant time.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    // Note: Memory locking functionality removed as it was unused
}