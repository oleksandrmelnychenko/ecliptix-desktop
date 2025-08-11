using System;
using Ecliptix.Core.Network.Core;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Core;

public interface IConnectionStateManager : IDisposable
{
    IObservable<ConnectionHealth> HealthChanged { get; }
    void RegisterConnection(uint connectId, ConnectionHealth initialHealth);
    void UpdateConnectionHealth(uint connectId, ConnectionHealthStatus status, NetworkFailure? failure = null);
    ConnectionHealth? GetConnectionHealth(uint connectId);
    void MarkConnectionRecovering(uint connectId, int attemptCount = 1);
    void RemoveConnection(uint connectId);
}