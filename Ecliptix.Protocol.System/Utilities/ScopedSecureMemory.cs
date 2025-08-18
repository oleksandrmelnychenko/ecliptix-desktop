using System.Buffers;
using System.Security.Cryptography;

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
        return size <= 0 ? throw new ArgumentException("Size must be positive", nameof(size)) : new ScopedSecureMemory(new byte[size]);
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

    public void Dispose()
    {
        if (_array != null)
        {
            _pool.Return(_array, clearArray: true);
        }
    }
}