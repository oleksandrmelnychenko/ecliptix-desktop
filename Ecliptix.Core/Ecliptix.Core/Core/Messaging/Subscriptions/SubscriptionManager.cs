using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Messaging.Subscriptions;

internal sealed class SubscriptionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, SubscriptionList> _subscriptions = new();
    private readonly Timer _cleanupTimer;
    private readonly Lock _cleanupLock = new();
    private long _totalSubscriptions;
    private long _deadReferencesCleanedUp;
    private volatile bool _disposed;

    public SubscriptionManager()
    {
        _cleanupTimer = new Timer(CleanupDeadReferences, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public IDisposable Subscribe<T>(
        Func<T, bool> filter,
        Func<T, Task> handler,
        SubscriptionLifetime lifetime,
        int priority) where T : class
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SubscriptionManager));
        }

        string messageTypeKey = GetMessageTypeKey<T>();
        SubscriptionList list = _subscriptions.GetOrAdd(messageTypeKey, _ => new SubscriptionList());

        ISubscription subscription = lifetime switch
        {
            SubscriptionLifetime.Weak => new WeakSubscription<T>(filter, handler, priority),
            SubscriptionLifetime.Strong => new StrongSubscription<T>(filter, handler, priority),
            SubscriptionLifetime.Scoped => new ScopedSubscription<T>(filter, handler, priority),
            _ => throw new ArgumentOutOfRangeException(nameof(lifetime))
        };

        list.Add(subscription);
        Interlocked.Increment(ref _totalSubscriptions);

        return new SubscriptionToken(() =>
        {
            list.Remove(subscription);
            Interlocked.Decrement(ref _totalSubscriptions);

            if (list.IsEmpty)
            {
                _subscriptions.TryRemove(messageTypeKey, out _);
            }
        });
    }

    public async Task PublishAsync<T>(T message, CancellationToken _) where T : class
    {
        if (_disposed || message == null)
        {
            return;
        }

        string messageTypeKey = GetMessageTypeKey<T>();

        if (!_subscriptions.TryGetValue(messageTypeKey, out SubscriptionList? list))
        {
            return;
        }

        ISubscription[] activeSubscriptions = list.GetActiveSubscriptions();
        if (activeSubscriptions.Length == 0)
        {
            return;
        }

        if (activeSubscriptions.Length > 1)
        {
            Array.Sort(activeSubscriptions, (x, y) => y.Priority.CompareTo(x.Priority));
        }

        List<Task> handlerTasks = new(activeSubscriptions.Length);

        foreach (ISubscription subscription in activeSubscriptions)
        {
            try
            {
                Task? handlerTask = subscription.HandleAsync(message);
                if (handlerTask != null)
                {
                    handlerTasks.Add(handlerTask);
                }
            }
            catch (Exception)
            {
                // Swallow handler invocation exceptions to allow other handlers to run
            }
        }

        if (handlerTasks.Count > 0)
        {
            try
            {
                await Task.WhenAll(handlerTasks);
            }
            catch (Exception)
            {
                // One or more handlers failed - individual handler exceptions are caught above
            }
        }
    }

    private void CleanupDeadReferences(object? state)
    {
        if (_disposed)
        {
            return;
        }

        lock (_cleanupLock)
        {
            if (_disposed)
            {
                return;
            }

            int cleanedUp = 0;
            List<string> emptyLists = [];

            foreach (KeyValuePair<string, SubscriptionList> kvp in _subscriptions)
            {
                cleanedUp += kvp.Value.CleanupDeadReferences();
                if (kvp.Value.IsEmpty)
                {
                    emptyLists.Add(kvp.Key);
                }
            }

            foreach (string key in emptyLists)
            {
                _subscriptions.TryRemove(key, out _);
            }

            Interlocked.Add(ref _deadReferencesCleanedUp, cleanedUp);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetMessageTypeKey<T>() where T : class
    {
        return typeof(T).Name;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _cleanupTimer.Dispose();

            foreach (SubscriptionList list in _subscriptions.Values)
            {
                list.Dispose();
            }

            _subscriptions.Clear();
        }
    }
}

internal sealed class SubscriptionToken(Action disposeAction) : IDisposable
{
    private bool _disposed;

    public void Dispose()
    {
        if (!_disposed)
        {
            disposeAction();
            _disposed = true;
        }
    }
}

internal sealed class SubscriptionList : IDisposable
{
    private readonly List<ISubscription> _subscriptions = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private bool _disposed;

    public void Add(ISubscription subscription)
    {
        _lock.EnterWriteLock();
        try
        {
            if (!_disposed)
            {
                _subscriptions.Add(subscription);
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Remove(ISubscription subscription)
    {
        _lock.EnterWriteLock();
        try
        {
            _subscriptions.Remove(subscription);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public ISubscription[] GetActiveSubscriptions()
    {
        _lock.EnterReadLock();
        try
        {
            return _subscriptions.Where(s => s.IsAlive).ToArray();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public int CleanupDeadReferences()
    {
        _lock.EnterWriteLock();
        try
        {
            int initialCount = _subscriptions.Count;
            _subscriptions.RemoveAll(s => !s.IsAlive);
            return initialCount - _subscriptions.Count;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public (int weak, int strong) GetSubscriptionCounts()
    {
        _lock.EnterReadLock();
        try
        {
            int weak = 0, strong = 0;
            foreach (ISubscription subscription in _subscriptions)
            {
                if (subscription.IsWeak)
                {
                    weak++;
                }
                else
                {
                    strong++;
                }
            }
            return (weak, strong);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool IsEmpty
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _subscriptions.Count == 0;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.EnterWriteLock();
            try
            {
                _subscriptions.Clear();
                _disposed = true;
            }
            finally
            {
                _lock.ExitWriteLock();
                _lock.Dispose();
            }
        }
    }
}
