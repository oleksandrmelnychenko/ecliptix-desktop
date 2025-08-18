using System.Buffers;

namespace Ecliptix.Protocol.System.Utilities;

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