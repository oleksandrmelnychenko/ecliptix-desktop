using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Protocol.System.Utilities;

/// <summary>
/// Provides automatic secure cleanup of memory on scope exit.
/// Use with 'using' statement to ensure memory is wiped even if exceptions occur.
/// </summary>
public sealed class ScopedSecureMemory : IDisposable
{
    private byte[]? _data;
    private readonly bool _clearOnDispose;
    private bool _disposed;

    private ScopedSecureMemory(byte[] data, bool clearOnDispose = true)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        _clearOnDispose = clearOnDispose;
    }

    /// <summary>
    /// Creates a new ScopedSecureMemory instance with the specified size.
    /// </summary>
    public static ScopedSecureMemory Allocate(int size)
    {
        if (size <= 0)
            throw new ArgumentException("Size must be positive", nameof(size));

        return new ScopedSecureMemory(new byte[size]);
    }

    /// <summary>
    /// Wraps an existing byte array for automatic cleanup.
    /// </summary>
    public static ScopedSecureMemory Wrap(byte[] data, bool clearOnDispose = true)
    {
        return new ScopedSecureMemory(data, clearOnDispose);
    }

    /// <summary>
    /// Gets the underlying data as a span.
    /// </summary>
    public Span<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _data!.AsSpan();
    }

    /// <summary>
    /// Gets the underlying data as a memory.
    /// </summary>
    public Memory<byte> AsMemory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _data!.AsMemory();
    }

    /// <summary>
    /// Gets the length of the data.
    /// </summary>
    public int Length => _data?.Length ?? 0;

    public void Dispose()
    {
        if (_disposed) return;

        if (_data != null && _clearOnDispose)
        {
            CryptographicOperations.ZeroMemory(_data);
        }

        _data = null;
        _disposed = true;
    }
}

/// <summary>
/// Manages a collection of secure memory buffers with automatic cleanup.
/// </summary>
public sealed class ScopedSecureMemoryCollection : IDisposable
{
    private readonly List<IDisposable> _resources = new();
    private bool _disposed;

    /// <summary>
    /// Allocates a new secure memory buffer and adds it to the collection.
    /// </summary>
    public ScopedSecureMemory Allocate(int size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var memory = ScopedSecureMemory.Allocate(size);
        _resources.Add(memory);
        return memory;
    }

    /// <summary>
    /// Allocates a secure memory handle and adds it to the collection.
    /// </summary>
    public Result<SodiumSecureMemoryHandle, SodiumFailure> AllocateHandle(int size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var result = SodiumSecureMemoryHandle.Allocate(size);
        if (result.IsOk)
        {
            _resources.Add(result.Unwrap());
        }
        return result;
    }

    /// <summary>
    /// Adds an existing disposable resource to the collection.
    /// </summary>
    public void Add(IDisposable resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resources.Add(resource ?? throw new ArgumentNullException(nameof(resource)));
    }

    public void Dispose()
    {
        if (_disposed) return;

        // Dispose in reverse order of allocation
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            try
            {
                _resources[i]?.Dispose();
            }
            catch
            {
                // Continue disposing other resources even if one fails
            }
        }

        _resources.Clear();
        _disposed = true;
    }
}

/// <summary>
/// Provides utilities for working with ArrayPool with automatic secure cleanup.
/// </summary>
public static class SecureArrayPool
{
    /// <summary>
    /// Rents an array from the pool and returns a wrapper that ensures cleanup.
    /// </summary>
    public static SecurePooledArray<T> Rent<T>(int minimumLength) where T : struct
    {
        return new SecurePooledArray<T>(minimumLength);
    }
}

/// <summary>
/// Wrapper for ArrayPool arrays that ensures they are cleared and returned.
/// </summary>
public readonly struct SecurePooledArray<T> : IDisposable where T : struct
{
    private readonly T[] _array;
    private readonly int _requestedLength;
    private readonly ArrayPool<T> _pool;

    internal SecurePooledArray(int minimumLength)
    {
        _pool = ArrayPool<T>.Shared;
        _array = _pool.Rent(minimumLength);
        _requestedLength = minimumLength;
    }

    public Span<T> AsSpan() => _array.AsSpan(0, _requestedLength);
    public Memory<T> AsMemory() => _array.AsMemory(0, _requestedLength);
    public int Length => _requestedLength;

    public void Dispose()
    {
        if (_array != null)
        {
            _pool.Return(_array, clearArray: true);
        }
    }
}

/// <summary>
/// Provides a using block that ensures sensitive data is cleared on exit.
/// </summary>
public static class SecureScope
{
    /// <summary>
    /// Executes an action with a secure buffer that is automatically cleared.
    /// </summary>
    public static void Execute(int bufferSize, Action<Span<byte>> action)
    {
        using var memory = ScopedSecureMemory.Allocate(bufferSize);
        action(memory.AsSpan());
    }

    /// <summary>
    /// Executes a function with a secure buffer that is automatically cleared.
    /// </summary>
    public static T Execute<T>(int bufferSize, Func<Span<byte>, T> func)
    {
        using var memory = ScopedSecureMemory.Allocate(bufferSize);
        return func(memory.AsSpan());
    }

    /// <summary>
    /// Executes an async action with a secure buffer that is automatically cleared.
    /// </summary>
    public static async Task ExecuteAsync(int bufferSize, Func<Memory<byte>, Task> action)
    {
        using var memory = ScopedSecureMemory.Allocate(bufferSize);
        await action(memory.AsMemory());
    }

    /// <summary>
    /// Executes an async function with a secure buffer that is automatically cleared.
    /// </summary>
    public static async Task<T> ExecuteAsync<T>(int bufferSize, Func<Memory<byte>, Task<T>> func)
    {
        using var memory = ScopedSecureMemory.Allocate(bufferSize);
        return await func(memory.AsMemory());
    }
}

/// <summary>
/// Extension methods for secure memory operations.
/// </summary>
public static class SecureMemoryExtensions
{
    /// <summary>
    /// Securely copies data to a new array and wipes the source.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] SecureCopy(this Span<byte> source)
    {
        var destination = new byte[source.Length];
        source.CopyTo(destination);
        CryptographicOperations.ZeroMemory(source);
        return destination;
    }

    /// <summary>
    /// Securely swaps two memory regions.
    /// </summary>
    public static void SecureSwap(this Span<byte> first, Span<byte> second)
    {
        if (first.Length != second.Length)
            throw new ArgumentException("Spans must have the same length");

        using var temp = ScopedSecureMemory.Allocate(first.Length);
        var tempSpan = temp.AsSpan();
        
        first.CopyTo(tempSpan);
        second.CopyTo(first);
        tempSpan.CopyTo(second);
    }

    /// <summary>
    /// Fills memory with cryptographically secure random bytes.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillRandom(this Span<byte> buffer)
    {
        RandomNumberGenerator.Fill(buffer);
    }

    /// <summary>
    /// Performs constant-time comparison of two memory regions.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool ConstantTimeEquals(this ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        if (first.Length != second.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(first, second);
    }
}