using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Communication;

public class ModuleMessageBus : IModuleMessageBus, IDisposable
{
    private readonly ILogger<ModuleMessageBus> _logger;
    private readonly ConcurrentDictionary<Type, ConcurrentBag<IMessageSubscription>> _subscriptions = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IModuleMessage>> _pendingRequests = new();
    private readonly Channel<IModuleMessage> _messageChannel;
    private readonly ChannelWriter<IModuleMessage> _messageWriter;
    private readonly Task _messageProcessingTask;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    private long _totalMessagesSent;
    private long _totalEventsPublished;
    private long _totalRequestsProcessed;
    private readonly ConcurrentQueue<double> _processingTimes = new();
    private const int MaxProcessingTimesSamples = 1000;

    public ModuleMessageBus(ILogger<ModuleMessageBus> logger)
    {
        _logger = logger;

        Channel<IModuleMessage> options = Channel.CreateUnbounded<IModuleMessage>();
        _messageChannel = options;
        _messageWriter = options.Writer;

        _messageProcessingTask = ProcessMessagesAsync(_cancellationTokenSource.Token);
    }

    public async Task PublishAsync<T>(T eventMessage, CancellationToken cancellationToken = default)
        where T : ModuleEvent
    {
        if (eventMessage == null) throw new ArgumentNullException(nameof(eventMessage));

        _logger.LogDebug("Publishing event {MessageType} from module {SourceModule}",
            eventMessage.MessageType, eventMessage.SourceModule);

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
            _logger.LogDebug("Sending request {MessageType} from {SourceModule} to {TargetModule}",
                request.MessageType, request.SourceModule, request.TargetModule);

            await _messageWriter.WriteAsync(request, cancellationToken);

            using CancellationTokenSource timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(request.Timeout);

            Task<IModuleMessage> responseTask = tcs.Task;
            Task timeoutTask = Task.Delay(request.Timeout, timeoutCts.Token);

            Task completedTask = await Task.WhenAny(responseTask, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Request {MessageId} timed out after {Timeout}",
                    request.MessageId, request.Timeout);
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

        _logger.LogDebug("Sending message {MessageType} from {SourceModule} to {TargetModule}",
            message.MessageType, message.SourceModule, message.TargetModule);

        await _messageWriter.WriteAsync(message, cancellationToken);
        Interlocked.Increment(ref _totalMessagesSent);
    }

    public IDisposable Subscribe<T>(Func<T, Task> handler) where T : IModuleMessage
    {
        return Subscribe<T>(_ => true, handler);
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

        _logger.LogDebug("Added subscription for message type {MessageType}", messageType.Name);

        return new SubscriptionDisposable(() =>
        {
            if (_subscriptions.TryGetValue(messageType, out ConcurrentBag<IMessageSubscription>? subscriptions))
            {
                ConcurrentBag<IMessageSubscription> newBag = new();
                foreach (IMessageSubscription sub in subscriptions.Where(s => s != subscription))
                {
                    newBag.Add(sub);
                }

                _subscriptions[messageType] = newBag;
            }

            _logger.LogDebug("Removed subscription for message type {MessageType}", messageType.Name);
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message {MessageType} from {SourceModule}",
                    message.MessageType, message.SourceModule);
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
        Type messageType = message.GetType();


        if (message is ModuleResponse response && !string.IsNullOrEmpty(response.CorrelationId))
        {
            if (_pendingRequests.TryGetValue(response.CorrelationId, out TaskCompletionSource<IModuleMessage>? tcs))
            {
                tcs.SetResult(response);
                return;
            }
        }

        HashSet<Type> typesToProcess = [messageType];

        foreach (Type interfaceType in messageType.GetInterfaces())
        {
            if (typeof(IModuleMessage).IsAssignableFrom(interfaceType))
            {
                typesToProcess.Add(interfaceType);
            }
        }

        Type? currentType = messageType.BaseType;
        while (currentType != null && typeof(IModuleMessage).IsAssignableFrom(currentType))
        {
            typesToProcess.Add(currentType);
            currentType = currentType.BaseType;
        }

        List<Task> handlerTasks = [];

        foreach (Type type in typesToProcess)
        {
            if (_subscriptions.TryGetValue(type, out ConcurrentBag<IMessageSubscription>? subscriptions))
            {
                foreach (IMessageSubscription subscription in subscriptions)
                {
                    try
                    {
                        Task handlerTask = subscription.HandleAsync(message);
                        handlerTasks.Add(handlerTask);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error invoking message handler for {MessageType}", type.Name);
                    }
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error waiting for message processing task to complete");
        }

        _cancellationTokenSource.Dispose();
    }
}

internal interface IMessageSubscription
{
    Task HandleAsync(IModuleMessage message);
}

internal class MessageSubscription<T>(Func<T, bool> filter, Func<T, Task> handler) : IMessageSubscription
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

internal class SubscriptionDisposable(Action disposeAction) : IDisposable
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