using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public sealed class SystemEventService(IUnifiedMessageBus messageBus) : ISystemEventService, IDisposable
{
    private readonly ReaderWriterLockSlim _stateLock = new();
    private SystemState _currentState = SystemState.Initializing;
    private string? _lastLogMessage;
    private bool _disposed;

    public SystemState CurrentState
    {
        get
        {
            _stateLock.EnterReadLock();
            try
            {
                return _currentState;
            }
            finally
            {
                _stateLock.ExitReadLock();
            }
        }
    }

    public async Task NotifySystemStateAsync(SystemState state, string? logMessage = null)
    {
        if (_disposed) return;

        _stateLock.EnterWriteLock();
        try
        {
            if (_currentState == state && string.Equals(_lastLogMessage, logMessage))
                return;

            if (!IsValidStateTransition(_currentState, state))
                return;

            _currentState = state;
            _lastLogMessage = logMessage;
        }
        finally
        {
            _stateLock.ExitWriteLock();
        }
        
        SystemStateChangedEvent evt = SystemStateChangedEvent.New(state, logMessage);
        await messageBus.PublishAsync(evt);
    }

    public IDisposable OnSystemStateChanged(
        Func<SystemStateChangedEvent, Task> handler, 
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak)
    {
        return messageBus.Subscribe(handler, lifetime);
    }

    private static bool IsValidStateTransition(SystemState current, SystemState next)
    {
        if (next == SystemState.FatalError || current == next)
            return true;

        if (current == SystemState.Initializing)
            return true;

        if (current == SystemState.FatalError)
            return false;

        return (current, next) switch
        {
            (SystemState.Running, SystemState.Busy) => true,
            (SystemState.Busy, SystemState.Running) => true,
            (SystemState.Running, SystemState.Recovering) => true,
            (SystemState.Recovering, SystemState.Running) => true,
            (SystemState.Running, SystemState.DataCenterShutdown) => true,
            _ => false
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _stateLock?.Dispose();
        }
    }
}