using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Messaging;

public interface IMessageBus : IDisposable
{
    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : class;

    IDisposable Subscribe<TMessage>(
        Func<TMessage, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.STRONG) where TMessage : class;
}

public enum SubscriptionLifetime
{
    STRONG,
    WEAK,
    SCOPED
}

public interface IMessageRequest
{
    string MessageId { get; }
    TimeSpan Timeout { get; }
}

public interface IMessageResponse
{
    string? CorrelationId { get; }
}

