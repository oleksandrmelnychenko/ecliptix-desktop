namespace Ecliptix.Core.Messaging.Core.Utilities;

internal sealed class DisposableAction(Action action) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        action();
    }
}
