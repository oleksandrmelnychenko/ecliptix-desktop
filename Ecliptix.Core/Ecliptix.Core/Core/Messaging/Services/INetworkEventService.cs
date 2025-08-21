using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public interface INetworkEventService
{
    Task NotifyNetworkStatusAsync(NetworkStatus status);

    Task RequestManualRetryAsync(uint? connectId = null);

    NetworkStatus CurrentStatus { get; }

    IDisposable OnNetworkStatusChanged(
        Func<NetworkStatusChangedEvent, Task> handler, 
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak);

    IDisposable OnManualRetryRequested(
        Func<ManualRetryRequestedEvent, Task> handler, 
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak);
}