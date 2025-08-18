using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Infrastructure.Network.Exceptions;

public class RequestPendingException : Exception
{
    public Task<object> PendingTask { get; }

    public RequestPendingException(Task<object> pendingTask)
        : base("Request queued for retry after server recovery")
    {
        PendingTask = pendingTask;
    }

    public RequestPendingException(Task<object> pendingTask, string message)
        : base(message)
    {
        PendingTask = pendingTask;
    }

    public RequestPendingException(Task<object> pendingTask, string message, Exception innerException)
        : base(message, innerException)
    {
        PendingTask = pendingTask;
    }
}