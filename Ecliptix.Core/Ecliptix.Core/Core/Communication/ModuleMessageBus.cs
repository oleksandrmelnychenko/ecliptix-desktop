using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Utilities;

namespace Ecliptix.Core.Core.Communication;

public sealed class ModuleMessageBus : IModuleMessageBus, IDisposable
{
    private const int MaxProcessingTimesSamples = 1000;

    private readonly ConcurrentDictionary<Type, ConcurrentBag<IMessageSubscription>> _subscriptions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IModuleMessage>> _pendingRequests = new();
    private readonly Channel<IModuleMessage> _messageChannel;
    private readonly ChannelWriter<IModuleMessage> _messageWriter;
    private readonly Task _messageProcessingTask;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly ConcurrentQueue<double> _processingTimes = new();
    private long _totalMessagesSent;
    private long _totalEventsPublished;
    private long _totalRequestsProcessed;

    public ModuleMessageBus()
    {
        Channel<IModuleMessage> options = Channel.CreateUnbounded<IModuleMessage>();
        _messageChannel = options;
        _messageWriter = options.Writer;

        _messageProcessingTask = ProcessMessagesAsync(_cancellationTokenSource.Token);
    }

    public async Task PublishAsync<T>(T eventMessage, CancellationToken cancellationToken = default)
        where T : ModuleEvent
    {
        if (eventMessage == null) throw new ArgumentNullException(nameof(eventMessage));

        await _messageWriter.WriteAsync(eventMessage, cancellationToken);
        Interlocked.Increment(ref _totalEventsPublished);
    }

    public async Task<TResponse?> RequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : ModuleRequest
        where TResponse : ModuleResponse
    {
        if (request == null) throw new ArgumentNullException(nameof(request));

        TaskCompletionSource<IModuleMessage> tcs = new();
        _pendingRequests[request.MessageId] = tcs;

        try
        {
            await _messageWriter.WriteAsync(request, cancellationToken);

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(request.Timeout);

            Task<IModuleMessage> responseTask = tcs.Task;
            Task timeoutTask = Task.Delay(request.Timeout, timeoutCts.Token);

            Task completedTask = await Task.WhenAny(responseTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                return null;
            }

            IModuleMessage response = await responseTask;
            Interlocked.Increment(ref _totalRequestsProcessed);

            return response as TResponse;
        }
        finally
        {
            _pendingRequests.TryRemove(request.MessageId, out _);
        }
    }

    public async Task SendAsync<T>(T message, CancellationToken cancellationToken = default) where T : IModuleMessage
    {
        if (message == null) throw new ArgumentNullException(nameof(message));

        await _messageWriter.WriteAsync(message, cancellationToken);
        Interlocked.Increment(ref _totalMessagesSent);
    }

    public IDisposable Subscribe<T>(Func<T, Task> handler) where T : IModuleMessage
    {
        return Subscribe(_ => true, handler);
    }

    public IDisposable Subscribe<T>(Func<T, bool> filter, Func<T, Task> handler) where T : IModuleMessage
    {
        Type messageType = typeof(T);
        MessageSubscription<T> subscription = new(filter, handler);

        _subscriptions.AddOrUpdate(messageType,
            _ => [subscription],
            (_, existing) =>
            {
                existing.Add(subscription);
                return existing;
            });

        return new DisposableAction(() =>
        {
            if (!_subscriptions.TryGetValue(messageType, out ConcurrentBag<IMessageSubscription>? subscriptions))
                return;
            ConcurrentBag<IMessageSubscription> newBag = [];
            foreach (IMessageSubscription sub in subscriptions.Where(s => s != subscription))
            {
                newBag.Add(sub);
            }

            _subscriptions[messageType] = newBag;
        });
    }

    public MessageBusStats GetStats()
    {
        int activeSubscriptions = _subscriptions.Values.Sum(bag => bag.Count);
        double avgProcessingTime = 0;

        if (_processingTimes.Count > 0)
        {
            avgProcessingTime = _processingTimes.ToArray().Average();
        }

        return new MessageBusStats
        {
            ActiveSubscriptions = activeSubscriptions,
            TotalMessagesSent = Interlocked.Read(ref _totalMessagesSent),
            TotalEventsPublished = Interlocked.Read(ref _totalEventsPublished),
            TotalRequestsProcessed = Interlocked.Read(ref _totalRequestsProcessed),
            AverageProcessingTimeMs = avgProcessingTime,
            PendingRequests = _pendingRequests.Count
        };
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        await foreach (IModuleMessage message in _messageChannel.Reader.ReadAllAsync(cancellationToken))
        {
            DateTime startTime = DateTime.UtcNow;

            try
            {
                await ProcessSingleMessageAsync(message);
            }
            catch (Exception)
            {
            }
            finally
            {
                double processingTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                RecordProcessingTime(processingTime);
            }
        }
    }

    private async Task ProcessSingleMessageAsync(IModuleMessage message)
    {
        if (message is ModuleResponse response && !string.IsNullOrEmpty(response.CorrelationId))
        {
            if (_pendingRequests.TryGetValue(response.CorrelationId, out TaskCompletionSource<IModuleMessage>? tcs))
            {
                tcs.SetResult(response);
                return;
            }
        }

        Type messageType = typeof(IModuleMessage);
        List<Task> handlerTasks = [];

        if (_subscriptions.TryGetValue(messageType, out ConcurrentBag<IMessageSubscription>? subscriptions))
        {
            foreach (IMessageSubscription subscription in subscriptions)
            {
                try
                {
                    Task handlerTask = subscription.HandleAsync(message);
                    handlerTasks.Add(handlerTask);
                }
                catch (Exception)
                {
                }
            }
        }

        if (handlerTasks.Count > 0)
        {
            await Task.WhenAll(handlerTasks);
        }
    }

    private void RecordProcessingTime(double processingTimeMs)
    {
        _processingTimes.Enqueue(processingTimeMs);

        while (_processingTimes.Count > MaxProcessingTimesSamples)
        {
            _processingTimes.TryDequeue(out _);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _messageWriter.Complete();

        try
        {
            _messageProcessingTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
        }

        _cancellationTokenSource.Dispose();
    }
}

internal interface IMessageSubscription
{
    Task HandleAsync(IModuleMessage message);
}

internal sealed class MessageSubscription<T>(Func<T, bool> filter, Func<T, Task> handler) : IMessageSubscription
    where T : IModuleMessage
{
    public async Task HandleAsync(IModuleMessage message)
    {
        if (message is T typedMessage && filter(typedMessage))
        {
            await handler(typedMessage);
        }
    }
}
