using System;

namespace Ecliptix.Core.Core.Utilities;

/// <summary>
/// Executes an action when disposed. Useful for cleanup operations in reactive subscriptions.
/// </summary>
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
