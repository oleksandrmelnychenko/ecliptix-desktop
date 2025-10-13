namespace Ecliptix.Protocol.System.Utilities;

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
                _resources[i].Dispose();
            }
            catch
            {
            }
        }

        _resources.Clear();
        _disposed = true;
    }
}
