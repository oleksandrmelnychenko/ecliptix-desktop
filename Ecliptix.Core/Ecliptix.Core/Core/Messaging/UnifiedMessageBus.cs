using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Subscriptions;

namespace Ecliptix.Core.Core.Messaging;

public sealed class UnifiedMessageBus : IUnifiedMessageBus
{
    private readonly SubscriptionManager _subscriptionManager;
    private readonly ConcurrentDictionary<string, IMessageRequest> _pendingRequests = new();

    private readonly ConcurrentDictionary<Type, ReactiveSubjectWrapper> _reactiveSubjects = new();
    private readonly Timer _subjectCleanupTimer;

    private long _totalMessagesPublished;
    private long _totalRequestsProcessed;

    private bool _disposed;

    public bool IsDisposed => _disposed;

    public UnifiedMessageBus()
    {
        _subscriptionManager = new SubscriptionManager();

        _subjectCleanupTimer = new Timer(CleanupUnusedSubjects, null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(2));
    }

    public IObservable<TMessage> GetEvent<TMessage>() where TMessage : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UnifiedMessageBus));

        ReactiveSubjectWrapper wrapper = _reactiveSubjects.GetOrAdd(
            typeof(TMessage),
            _ => new ReactiveSubjectWrapper(new Subject<TMessage>()));

        wrapper.IncrementReference();

        IObservable<TMessage> observable = ((Subject<TMessage>)wrapper.Subject).AsObservable();

        return Observable.Create<TMessage>(observer =>
        {
            IDisposable subscription = observable.Subscribe(observer);
            return new CompositeDisposable(subscription, new DisposableAction(() => wrapper.DecrementReference()));
        });
    }

    public void Publish<TMessage>(TMessage message) where TMessage : class
    {
        if (_disposed || message == null) return;

        if (_reactiveSubjects.TryGetValue(typeof(TMessage), out ReactiveSubjectWrapper? wrapper))
        {
            try
            {
                ((Subject<TMessage>)wrapper.Subject).OnNext(message);
            }
            catch (Exception)
            {
            }
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await _subscriptionManager.PublishAsync(message, CancellationToken.None);
            }
            catch (Exception)
            {
            }
        });

        Interlocked.Increment(ref _totalMessagesPublished);
    }

    public async Task PublishAsync<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : class
    {
        if (_disposed || message == null) return;

        if (_reactiveSubjects.TryGetValue(typeof(TMessage), out ReactiveSubjectWrapper? wrapper))
        {
            try
            {
                ((Subject<TMessage>)wrapper.Subject).OnNext(message);
            }
            catch (Exception)
            {
            }
        }

        await _subscriptionManager.PublishAsync(message, cancellationToken);

        Interlocked.Increment(ref _totalMessagesPublished);
    }

    public IDisposable Subscribe<TMessage>(
        Func<TMessage, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Strong) where TMessage : class
    {
        return Subscribe<TMessage>(_ => true, handler, lifetime, 0);
    }

    public IDisposable Subscribe<TMessage>(
        Func<TMessage, bool> filter,
        Func<TMessage, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Strong,
        int priority = 0) where TMessage : class
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UnifiedMessageBus));

        return _subscriptionManager.Subscribe(filter, handler, lifetime, priority);
    }

    public async Task<TResponse?> RequestAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan timeout = default,
        CancellationToken cancellationToken = default)
        where TRequest : class, IMessageRequest
        where TResponse : class, IMessageResponse
    {
        if (_disposed) throw new ObjectDisposedException(nameof(UnifiedMessageBus));
        if (request == null) throw new ArgumentNullException(nameof(request));

        if (timeout == default)
            timeout = request.Timeout;

        TaskCompletionSource<IMessageResponse> tcs = new();
        _pendingRequests[request.MessageId] = request;

        try
        {
            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            IDisposable responseSubscription = Subscribe<TResponse>(
                response => response.CorrelationId == request.MessageId,
                async response =>
                {
                    tcs.SetResult(response);
                    await Task.CompletedTask;
                },
                SubscriptionLifetime.Scoped);

            await PublishAsync(request, cancellationToken);

            Task<IMessageResponse> responseTask = tcs.Task;
            Task timeoutTask = Task.Delay(timeout, timeoutCts.Token);

            Task completedTask = await Task.WhenAny(responseTask, timeoutTask);

            responseSubscription.Dispose();

            if (completedTask == timeoutTask)
            {
                return null;
            }

            IMessageResponse response = await responseTask;
            Interlocked.Increment(ref _totalRequestsProcessed);

            return response as TResponse;
        }
        finally
        {
            _pendingRequests.TryRemove(request.MessageId, out _);
        }
    }

    public MessageBusStatistics GetStatistics()
    {
        if (_disposed) return new MessageBusStatistics();

        MessageBusStatistics baseStats = _subscriptionManager.GetStatistics();

        int activeSubjects = _reactiveSubjects.Count(kvp => kvp.Value.ReferenceCount > 0);

        return baseStats with
        {
            TotalMessagesPublished = Interlocked.Read(ref _totalMessagesPublished),
            TotalRequestsProcessed = Interlocked.Read(ref _totalRequestsProcessed),
            PooledObjectsInUse = activeSubjects
        };
    }

    private void CleanupUnusedSubjects(object? state)
    {
        if (_disposed) return;

        foreach (KeyValuePair<Type, ReactiveSubjectWrapper> kvp in _reactiveSubjects.ToArray())
        {
            if (kvp.Value.ReferenceCount == 0 && kvp.Value.IsExpired)
            {
                if (_reactiveSubjects.TryRemove(kvp.Key, out ReactiveSubjectWrapper? removed))
                {
                    removed.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;

            _subjectCleanupTimer?.Dispose();

            foreach (ReactiveSubjectWrapper wrapper in _reactiveSubjects.Values)
            {
                wrapper.Dispose();
            }

            _reactiveSubjects.Clear();

            _subscriptionManager.Dispose();

            _pendingRequests.Clear();
        }
    }
}

internal sealed class ReactiveSubjectWrapper(object subject) : IDisposable
{
    private int _referenceCount;
    private readonly DateTime _createdAt = DateTime.UtcNow;
    private bool _disposed;

    public object Subject { get; } = subject;
    public int ReferenceCount => _referenceCount;
    public bool IsExpired => DateTime.UtcNow - _createdAt > TimeSpan.FromMinutes(5);

    public void IncrementReference()
    {
        if (!_disposed)
            Interlocked.Increment(ref _referenceCount);
    }

    public void DecrementReference()
    {
        if (!_disposed)
            Interlocked.Decrement(ref _referenceCount);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (Subject is Subject<object> subject)
            {
                subject.OnCompleted();
                subject.Dispose();
            }
        }
    }
}

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