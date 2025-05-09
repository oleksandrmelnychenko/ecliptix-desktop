using System;

namespace Ecliptix.Core.Network;

public class RetryableRequest
{
    /// <summary>
    /// The retryable action to be executed.
    /// </summary>
    public RetryableAction Action { get; }

    /// <summary>
    /// The timestamp when the task was created.
    /// </summary>
    public DateTime CreatedAt { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryableRequest"/> class.
    /// </summary>
    /// <param name="action">The retryable action.</param>
    /// <param name="createdAt">The creation timestamp.</param>
    public RetryableRequest(RetryableAction action, DateTime createdAt)
    {
        Action = action;
        CreatedAt = createdAt;
    }
}