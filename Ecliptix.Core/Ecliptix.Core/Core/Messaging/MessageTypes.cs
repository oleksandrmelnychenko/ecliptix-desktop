using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Ecliptix.Core.Core.Messaging;

public abstract record SimpleMessage
{
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public abstract record MessageRequest : SimpleMessage, IMessageRequest
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

public abstract record MessageResponse : SimpleMessage, IMessageResponse
{
    public string MessageId { get; init; } = Guid.NewGuid().ToString();
    public string? CorrelationId { get; init; }
    public bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class MessageEnvelope<T> : IDisposable where T : class
{
    private static readonly ObjectPool<MessageEnvelope<T>> Pool = new();
    private bool _disposed;

    public T Message { get; private set; } = null!;
    public DateTime Timestamp { get; private set; }
    public string MessageId { get; private set; } = string.Empty;
    public string? SourceId { get; private set; }
    public int Priority { get; private set; }
    public Dictionary<string, object>? Metadata { get; private set; }

    public static MessageEnvelope<T> Create(T message, string? sourceId = null, int priority = 0,
        Dictionary<string, object>? metadata = null)
    {
        MessageEnvelope<T> envelope = Pool.Get();
        envelope._disposed = false;
        envelope.Message = message;
        envelope.Timestamp = DateTime.UtcNow;
        envelope.MessageId = Guid.NewGuid().ToString();
        envelope.SourceId = sourceId;
        envelope.Priority = priority;
        envelope.Metadata = metadata;
        return envelope;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Message = default!;
            SourceId = null;
            Metadata?.Clear();
            _disposed = true;
            Pool.Return(this);
        }
    }
}

internal sealed class ObjectPool<T> where T : class, new()
{
    private readonly ConcurrentStack<T> _objects = new();
    private int _currentCount;
    private const int MaxSize = 100;

    public T Get()
    {
        if (_objects.TryPop(out T? item))
        {
            Interlocked.Decrement(ref _currentCount);
            return item;
        }

        return new T();
    }

    public void Return(T item)
    {
        if (_currentCount < MaxSize)
        {
            _objects.Push(item);
            Interlocked.Increment(ref _currentCount);
        }
    }

    public int Count => _currentCount;
}