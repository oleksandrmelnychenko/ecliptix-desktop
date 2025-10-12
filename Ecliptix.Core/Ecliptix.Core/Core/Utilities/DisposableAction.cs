using System;

namespace Ecliptix.Core.Core.Utilities;

internal sealed class DisposableAction(Action action) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            action();
        }
    }
}
