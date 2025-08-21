using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Messaging.Subscriptions;

internal interface ISubscription
{
    int Priority { get; }
    bool IsAlive { get; }
    bool IsWeak { get; }
    Task? HandleAsync(object message);
}

internal sealed class StrongSubscription<T>(Func<T, bool> filter, Func<T, Task> handler, int priority) : ISubscription
    where T : class
{
    public int Priority { get; } = priority;
    public bool IsAlive => true; 
    public bool IsWeak => false;

    public Task? HandleAsync(object message)
    {
        if (message is T typedMessage && filter(typedMessage))
        {
            return handler(typedMessage);
        }
        return null;
    }
}

internal sealed class WeakSubscription<T>(Func<T, bool> filter, Func<T, Task> handler, int priority) : ISubscription
    where T : class
{
    private readonly WeakReference<Func<T, Task>> _handlerRef = new(handler);

    public int Priority { get; } = priority;
    public bool IsWeak => true;

    public bool IsAlive => _handlerRef.TryGetTarget(out _);

    public Task? HandleAsync(object message)
    {
        if (message is T typedMessage && 
            filter(typedMessage) && 
            _handlerRef.TryGetTarget(out Func<T, Task>? handler))
        {
            return handler(typedMessage);
        }
        return null;
    }
}

internal sealed class ScopedSubscription<T>(Func<T, bool> filter, Func<T, Task> handler, int priority)
    : ISubscription, IDisposable
    where T : class
{
    private bool _disposed;

    public int Priority { get; } = priority;
    public bool IsAlive => !_disposed;
    public bool IsWeak => false;

    public Task? HandleAsync(object message)
    {
        if (!_disposed && message is T typedMessage && filter(typedMessage))
        {
            return handler(typedMessage);
        }
        return null;
    }

    public void Dispose()
    {
        _disposed = true;
    }
}

public static class SubscriptionExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IDisposable DisposeWith(this IDisposable subscription, IDisposableCollection disposables)
    {
        disposables.Add(subscription);
        return subscription;
    }
}

public interface IDisposableCollection
{
    void Add(IDisposable disposable);
}