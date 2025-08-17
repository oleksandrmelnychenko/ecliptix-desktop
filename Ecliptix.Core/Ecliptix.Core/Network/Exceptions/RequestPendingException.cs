using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Network.Exceptions;

/// <summary>
/// Exception thrown when a request is queued for retry after server shutdown is detected.
/// This allows the UI to remain responsive while the request waits for server recovery.
/// </summary>
public class RequestPendingException : Exception
{
    /// <summary>
    /// The pending task that will complete when the server recovers and the request is retried.
    /// </summary>
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