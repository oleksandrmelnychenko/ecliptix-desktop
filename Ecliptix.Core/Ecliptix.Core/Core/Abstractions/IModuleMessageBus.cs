using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Abstractions;
public interface IModuleMessageBus
{



    Task PublishAsync<T>(T eventMessage, CancellationToken cancellationToken = default)
        where T : ModuleEvent;




    Task<TResponse?> RequestAsync<TRequest, TResponse>(
        TRequest request,
        CancellationToken cancellationToken = default)
        where TRequest : ModuleRequest
        where TResponse : ModuleResponse;




    Task SendAsync<T>(T message, CancellationToken cancellationToken = default)
        where T : IModuleMessage;




    IDisposable Subscribe<T>(Func<T, Task> handler) where T : IModuleMessage;




    IDisposable Subscribe<T>(Func<T, bool> filter, Func<T, Task> handler) where T : IModuleMessage;




    MessageBusStats GetStats();
}
public interface IModuleMessageHandler<in T> where T : IModuleMessage
{
    Task HandleAsync(T message, CancellationToken cancellationToken = default);
}
public record MessageBusStats
{
    public int ActiveSubscriptions { get; init; }
    public long TotalMessagesSent { get; init; }
    public long TotalEventsPublished { get; init; }
    public long TotalRequestsProcessed { get; init; }
    public double AverageProcessingTimeMs { get; init; }
    public int PendingRequests { get; init; }
}