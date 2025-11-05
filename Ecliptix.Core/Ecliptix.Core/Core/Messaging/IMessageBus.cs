using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Messaging;

public interface IMessageBus : IDisposable
{
    IObservable<TMessage> GetEvent<TMessage>() where TMessage : class;

    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : class;

    IDisposable Subscribe<TMessage>(
        Func<TMessage, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Strong) where TMessage : class;
}

public enum SubscriptionLifetime
{
    Strong,

    Weak,

    Scoped
}

public interface IMessageRequest
{
    string MessageId { get; }
    TimeSpan Timeout { get; }
}

public interface IMessageResponse
{
    string? CORRELATION_ID { get; }
}

