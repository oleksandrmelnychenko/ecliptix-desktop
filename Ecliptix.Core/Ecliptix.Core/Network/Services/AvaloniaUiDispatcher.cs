using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Network.Contracts.Services;
using Serilog;

namespace Ecliptix.Core.Network.Services;

/// <summary>
/// Avalonia UI framework implementation of IUiDispatcher.
/// Handles dispatching actions to the Avalonia UI thread safely.
/// </summary>
public class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool IsAvailable => true; // Always available for Avalonia UI

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            if (!IsAvailable)
            {
                Log.Debug("UI Dispatcher not available, skipping UI action");
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                // Already on UI thread, execute directly
                action();
            }
            else
            {
                // Dispatch to UI thread
                Dispatcher.UIThread.Post(action);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to dispatch action to UI thread");
        }
    }

    public async Task PostAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        try
        {
            if (!IsAvailable)
            {
                Log.Debug("UI Dispatcher not available, skipping async UI action");
                return;
            }

            if (Dispatcher.UIThread.CheckAccess())
            {
                // Already on UI thread, execute directly
                await action();
            }
            else
            {
                // Dispatch to UI thread and wait for completion
                await Dispatcher.UIThread.InvokeAsync(action);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to dispatch async action to UI thread");
        }
    }
}