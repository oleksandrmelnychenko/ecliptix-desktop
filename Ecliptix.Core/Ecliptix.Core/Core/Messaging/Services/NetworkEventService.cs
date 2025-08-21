using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public sealed class NetworkEventService(IUnifiedMessageBus messageBus) : INetworkEventService, IDisposable
{
    private readonly ReaderWriterLockSlim _statusLock = new();
    private NetworkStatus _currentStatus = NetworkStatus.DataCenterDisconnected;
    private DateTime _lastStatusChangeTime = DateTime.UtcNow;
    private int _retryCount;
    private bool _disposed;

    public NetworkStatus CurrentStatus
    {
        get
        {
            _statusLock.EnterReadLock();
            try
            {
                return _currentStatus;
            }
            finally
            {
                _statusLock.ExitReadLock();
            }
        }
    }

    public async Task NotifyNetworkStatusAsync(NetworkStatus status)
    {
        if (_disposed) return;

        _statusLock.EnterWriteLock();
        try
        {
            if (_currentStatus == status)
                return;

            if (!IsValidStatusTransition(_currentStatus, status))
                return;

            _currentStatus = status;
            _lastStatusChangeTime = DateTime.UtcNow;

            _retryCount = status switch
            {
                NetworkStatus.DataCenterConnected or NetworkStatus.ConnectionRestored => 0,
                NetworkStatus.DataCenterConnecting => _retryCount + 1,
                _ => _retryCount
            };
        }
        finally
        {
            _statusLock.ExitWriteLock();
        }
        
        NetworkStatusChangedEvent evt = NetworkStatusChangedEvent.New(status);
        await messageBus.PublishAsync(evt);
    }

    public async Task RequestManualRetryAsync(uint? connectId = null)
    {
        if (_disposed) return;

        _statusLock.EnterWriteLock();
        try
        {
            _retryCount = 0;
        }
        finally
        {
            _statusLock.ExitWriteLock();
        }

        ManualRetryRequestedEvent evt = ManualRetryRequestedEvent.New(connectId);
        await messageBus.PublishAsync(evt);
    }

    public IDisposable OnNetworkStatusChanged(
        Func<NetworkStatusChangedEvent, Task> handler, 
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak)
    {
        return messageBus.Subscribe(handler, lifetime);
    }

    public IDisposable OnManualRetryRequested(
        Func<ManualRetryRequestedEvent, Task> handler, 
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak)
    {
        return messageBus.Subscribe(handler, lifetime);
    }

    private static bool IsValidStatusTransition(NetworkStatus current, NetworkStatus next)
    {
        if (current == next)
            return true;

        if (next is NetworkStatus.ServerShutdown or NetworkStatus.RetriesExhausted)
            return true;

        return (current, next) switch
        {
            (NetworkStatus.DataCenterDisconnected, NetworkStatus.DataCenterConnecting) => true,
            (NetworkStatus.DataCenterConnecting, NetworkStatus.DataCenterConnected) => true,
            (NetworkStatus.DataCenterConnecting, NetworkStatus.DataCenterDisconnected) => true,
            (NetworkStatus.DataCenterConnected, NetworkStatus.DataCenterDisconnected) => true,
            (NetworkStatus.DataCenterDisconnected, NetworkStatus.ConnectionRecovering) => true,
            (NetworkStatus.ConnectionRecovering, NetworkStatus.ConnectionRestored) => true,
            (NetworkStatus.ConnectionRecovering, NetworkStatus.DataCenterDisconnected) => true,
            _ => false
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _statusLock?.Dispose();
        }
    }
}