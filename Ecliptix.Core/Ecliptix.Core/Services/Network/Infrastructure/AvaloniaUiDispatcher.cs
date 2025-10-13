using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using Ecliptix.Core.Services.Abstractions.Network;
using Serilog;

namespace Ecliptix.Core.Services.Network.Infrastructure;

public class AvaloniaUiDispatcher : IUiDispatcher
{
    public bool IsAvailable => true;

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
                action();
            }
            else
            {
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
                await action();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(action);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to dispatch async action to UI thread");
        }
    }
}
