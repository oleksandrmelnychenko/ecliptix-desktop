using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Utilities;

public static class TaskExtensions
{
    public static async void DoSafeAsync(
        this Task task,
        Action<Exception>? onException = null)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            onException?.Invoke(ex);
        }
    }
}
