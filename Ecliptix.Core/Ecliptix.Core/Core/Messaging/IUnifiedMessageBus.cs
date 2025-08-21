using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Messaging;

public interface IUnifiedMessageBus : IDisposable
{
    IObservable<TMessage> GetEvent<TMessage>() where TMessage : class;

    void Publish<TMessage>(TMessage message) where TMessage : class;

    Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : class;

    IDisposable Subscribe<TMessage>(
        Func<TMessage, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Strong) where TMessage : class;

    IDisposable Subscribe<TMessage>(
        Func<TMessage, bool> filter,
        Func<TMessage, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Strong,
        int priority = 0) where TMessage : class;

    Task<TResponse?> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
        where TRequest : class, IMessageRequest
        where TResponse : class, IMessageResponse;

    MessageBusStatistics GetStatistics();

    bool IsDisposed { get; }
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
    DateTime Timestamp { get; }
    TimeSpan Timeout { get; }
}

public interface IMessageResponse
{
    string MessageId { get; }
    string? CorrelationId { get; }
    DateTime Timestamp { get; }
    bool IsSuccess { get; }
    string? ErrorMessage { get; }
}

public record MessageBusStatistics
{
    public int ActiveSubscriptions { get; init; }
    public int WeakSubscriptions { get; init; }
    public int StrongSubscriptions { get; init; }
    public long TotalMessagesPublished { get; init; }
    public long TotalRequestsProcessed { get; init; }
    public double AverageProcessingTimeMs { get; init; }
    public int DeadReferencesCleanedUp { get; init; }
    public int PooledObjectsInUse { get; init; }
    public int PooledObjectsAvailable { get; init; }
}