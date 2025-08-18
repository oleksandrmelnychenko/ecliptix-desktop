using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Protocol.System.Utilities;

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

    public static ScopedSecureMemory Allocate(int size)
    {
        if (size <= 0)
            throw new ArgumentException("Size must be positive", nameof(size));

        return new ScopedSecureMemory(new byte[size]);
    }

    public static ScopedSecureMemory Wrap(byte[] data, bool clearOnDispose = true)
    {
        return new ScopedSecureMemory(data, clearOnDispose);
    }

    public Span<byte> AsSpan()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _data!.AsSpan();
    }

    public Memory<byte> AsMemory()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _data!.AsMemory();
    }

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

public sealed class ScopedSecureMemoryCollection : IDisposable
{
    private readonly List<IDisposable> _resources = new();
    private bool _disposed;

    public ScopedSecureMemory Allocate(int size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        ScopedSecureMemory memory = ScopedSecureMemory.Allocate(size);
        _resources.Add(memory);
        return memory;
    }

    public Result<SodiumSecureMemoryHandle, SodiumFailure> AllocateHandle(int size)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        Result<SodiumSecureMemoryHandle, SodiumFailure> result = SodiumSecureMemoryHandle.Allocate(size);
        if (result.IsOk)
        {
            _resources.Add(result.Unwrap());
        }
        return result;
    }

    public void Add(IDisposable resource)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _resources.Add(resource ?? throw new ArgumentNullException(nameof(resource)));
    }

    public void Dispose()
    {
        if (_disposed) return;

        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            try
            {
                _resources[i]?.Dispose();
            }
            catch
            {
            }
        }

        _resources.Clear();
        _disposed = true;
    }
}

public static class SecureArrayPool
{
    public static SecurePooledArray<T> Rent<T>(int minimumLength) where T : struct
    {
        return new SecurePooledArray<T>(minimumLength);
    }
}

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

public static class SecureScope
{
    public static void Execute(int bufferSize, Action<Span<byte>> action)
    {
        using ScopedSecureMemory memory = ScopedSecureMemory.Allocate(bufferSize);
        action(memory.AsSpan());
    }

    public static T Execute<T>(int bufferSize, Func<Span<byte>, T> func)
    {
        using ScopedSecureMemory memory = ScopedSecureMemory.Allocate(bufferSize);
        return func(memory.AsSpan());
    }

    public static async Task ExecuteAsync(int bufferSize, Func<Memory<byte>, Task> action)
    {
        using ScopedSecureMemory memory = ScopedSecureMemory.Allocate(bufferSize);
        await action(memory.AsMemory());
    }

    public static async Task<T> ExecuteAsync<T>(int bufferSize, Func<Memory<byte>, Task<T>> func)
    {
        using ScopedSecureMemory memory = ScopedSecureMemory.Allocate(bufferSize);
        return await func(memory.AsMemory());
    }
}

public static class SecureMemoryExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] SecureCopy(this Span<byte> source)
    {
        byte[] destination = new byte[source.Length];
        source.CopyTo(destination);
        CryptographicOperations.ZeroMemory(source);
        return destination;
    }

    public static void SecureSwap(this Span<byte> first, Span<byte> second)
    {
        if (first.Length != second.Length)
            throw new ArgumentException("Spans must have the same length");

        using ScopedSecureMemory temp = ScopedSecureMemory.Allocate(first.Length);
        Span<byte> tempSpan = temp.AsSpan();

        first.CopyTo(tempSpan);
        second.CopyTo(first);
        tempSpan.CopyTo(second);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void FillRandom(this Span<byte> buffer)
    {
        RandomNumberGenerator.Fill(buffer);
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool ConstantTimeEquals(this ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        if (first.Length != second.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(first, second);
    }
}