using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public interface ISystemEventService
{
    Task NotifySystemStateAsync(SystemState state, string? logMessage = null);

    SystemState CurrentState { get; }

    IDisposable OnSystemStateChanged(
        Func<SystemStateChangedEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak);
}