using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Network.Contracts.Core;
using Ecliptix.Core.Network.Core.Configuration;
using Ecliptix.Core.Network.Services.Queue;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Core;

public class ConnectionStateManager : IConnectionStateManager
{
    private readonly ConnectionStateConfiguration _config;
    private readonly ConcurrentDictionary<uint, ConnectionHealth> _connections = new();
    private readonly Subject<ConnectionHealth> _healthChangedSubject = new();
    private readonly Timer _healthCheckTimer;
    private readonly Timer? _autoRecoveryTimer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public IObservable<ConnectionHealth> HealthChanged => _healthChangedSubject.AsObservable();

    public ConnectionStateManager(ConnectionStateConfiguration config)
    {
        _config = config;

        _healthCheckTimer = new Timer(PerformHealthCheck, null, 
            _config.HealthCheckInterval, _config.HealthCheckInterval);

        if (_config.AutoRecoveryEnabled)
        {
            _autoRecoveryTimer = new Timer(PerformAutoRecovery, null,
                _config.AutoRecoveryInterval, _config.AutoRecoveryInterval);
        }

    }

    private ConnectionHealth UpdateHealthMetrics(ConnectionHealth current, OperationType operation,
        Result<Unit, NetworkFailure> result, TimeSpan latency)
    {
        DateTime now = DateTime.UtcNow;
        ConnectionHealthMetrics metrics = current.Metrics;

        Dictionary<OperationType, DateTime> operationTimes = new(metrics.LastOperationTimes)
        {
            [operation] = now
        };

        ConnectionHealthMetrics updatedMetrics;
        ConnectionHealthStatus newStatus;

        if (result.IsOk)
        {
            updatedMetrics = metrics with
            {
                ConsecutiveSuccesses = metrics.ConsecutiveSuccesses + 1,
                ConsecutiveFailures = 0,
                AverageLatency = CalculateAverageLatency(metrics.AverageLatency, latency),
                SuccessRate = CalculateSuccessRate(metrics, true),
                LastOperationTimes = operationTimes
            };

            newStatus = DetermineHealthStatus(updatedMetrics);
        }
        else
        {
            FailureCategory failureCategory = CategorizeFailure(result.UnwrapErr());
            Dictionary<FailureCategory, int> failureCounts = new(metrics.FailureCounts);
            failureCounts[failureCategory] = failureCounts.GetValueOrDefault(failureCategory) + 1;

            updatedMetrics = metrics with
            {
                ConsecutiveFailures = metrics.ConsecutiveFailures + 1,
                ConsecutiveSuccesses = 0,
                SuccessRate = CalculateSuccessRate(metrics, false),
                LastOperationTimes = operationTimes,
                FailureCounts = failureCounts
            };

            newStatus = DetermineHealthStatus(updatedMetrics);
        }

        return current with
        {
            Metrics = updatedMetrics,
            Status = newStatus,
            LastHealthCheck = now,
            LastError = result.IsErr ? result.UnwrapErr().Message : null
        };
    }

    private ConnectionHealthStatus DetermineHealthStatus(ConnectionHealthMetrics metrics)
    {
        if (metrics.ConsecutiveFailures >= _config.MaxConsecutiveFailures)
            return ConnectionHealthStatus.Failed;

        if (metrics.SuccessRate < _config.MinimumSuccessRate)
            return ConnectionHealthStatus.Unhealthy;

        if (metrics.ConsecutiveFailures > 0 || metrics.SuccessRate < 0.95)
            return ConnectionHealthStatus.Degraded;

        return ConnectionHealthStatus.Healthy;
    }

    private static FailureCategory CategorizeFailure(NetworkFailure failure)
    {
        string message = failure.Message.ToLowerInvariant();

        return message switch
        {
            not null when message.Contains("network") || message.Contains("connection") || message.Contains("unreachable") 
                => FailureCategory.NetworkConnectivity,
            not null when message.Contains("timeout") || message.Contains("deadline") 
                => FailureCategory.Timeout,
            not null when message.Contains("rate") || message.Contains("limit") || message.Contains("throttl") 
                => FailureCategory.RateLimit,
            not null when message.Contains("auth") || message.Contains("unauthorized") || message.Contains("forbidden") 
                => FailureCategory.Authentication,
            not null when message.Contains("server") || message.Contains("internal") || message.Contains("500") 
                => FailureCategory.ServerError,
            not null when message.Contains("protocol") || message.Contains("invalid") 
                => FailureCategory.Protocol,
            not null when message.Contains("desync") || message.Contains("decrypt") || message.Contains("rekey")
                => FailureCategory.CryptographicDesync,
            _ => FailureCategory.Unknown
        };
    }

    private static TimeSpan CalculateAverageLatency(TimeSpan current, TimeSpan newLatency)
    {
        if (current == TimeSpan.Zero)
            return newLatency;

        const double alpha = 0.2;
        double currentMs = current.TotalMilliseconds;
        double newMs = newLatency.TotalMilliseconds;
        double averageMs = (alpha * newMs) + ((1 - alpha) * currentMs);
        
        return TimeSpan.FromMilliseconds(averageMs);
    }

    private static double CalculateSuccessRate(ConnectionHealthMetrics metrics, bool wasSuccess)
    {
        const double alpha = 0.1;
        double currentRate = metrics.SuccessRate;
        double newValue = wasSuccess ? 1.0 : 0.0;
        
        return alpha * newValue + (1 - alpha) * currentRate;
    }

    public ConnectionHealth? GetConnectionHealth(uint connectId)
    {
        return _connections.GetValueOrDefault(connectId);
    }

    public void MarkConnectionRecovering(uint connectId, int attemptCount = 1)
    {
        if (_connections.TryGetValue(connectId, out ConnectionHealth? current))
        {
            ConnectionHealth updated = current with
            {
                IsRecovering = true,
                RecoveryAttempts = attemptCount,
                Status = ConnectionHealthStatus.Reconnecting
            };
            
            _connections.TryUpdate(connectId, updated, current);
            _healthChangedSubject.OnNext(updated);
        }
    }

    public void RemoveConnection(uint connectId)
    {
        _connections.TryRemove(connectId, out _);
    }

    private void PerformHealthCheck(object? state)
    {
        try
        {
            DateTime now = DateTime.UtcNow;
            List<ConnectionHealth> staleConnections = _connections.Values
                .Where(h => now - h.LastHealthCheck > _config.MaxUnhealthyDuration)
                .ToList();

            foreach (ConnectionHealth connection in staleConnections)
            {
                if (connection.Status != ConnectionHealthStatus.Failed)
                {
                    ConnectionHealth updated = connection with
                    {
                        Status = ConnectionHealthStatus.Disconnected,
                        LastHealthCheck = now
                    };

                    _connections.TryUpdate(connection.ConnectId, updated, connection);
                    _healthChangedSubject.OnNext(updated);
                    
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private void PerformAutoRecovery(object? state)
    {
        if (!_config.AutoRecoveryEnabled)
            return;

        try
        {
            List<ConnectionHealth> unhealthyConnections = _connections.Values
                .Where(h => h.Status is ConnectionHealthStatus.Unhealthy or ConnectionHealthStatus.Failed 
                    && !h.IsRecovering)
                .ToList();

            foreach (ConnectionHealth connection in unhealthyConnections)
            {
                
                MarkConnectionRecovering(connection.ConnectId);
            }
        }
        catch (Exception)
        {
        }
    }

    public void RegisterConnection(uint connectId, ConnectionHealth initialHealth)
    {
        _connections.AddOrUpdate(connectId, initialHealth, (id, existing) => initialHealth);
        _healthChangedSubject.OnNext(initialHealth);
    }

    public void UpdateConnectionHealth(uint connectId, ConnectionHealthStatus status, NetworkFailure? failure = null)
    {
        if (!_connections.TryGetValue(connectId, out ConnectionHealth? current))
        {
            ConnectionHealth defaultHealth = new()
            {
                ConnectId = connectId,
                Status = ConnectionHealthStatus.Unknown,
                LastHealthCheck = DateTime.UtcNow
            };
            RegisterConnection(connectId, defaultHealth);
            current = defaultHealth;
        }

        ConnectionHealth updated = current with
        {
            Status = status,
            LastHealthCheck = DateTime.UtcNow,
            LastError = failure?.Message
        };

        _connections.TryUpdate(connectId, updated, current);
        _healthChangedSubject.OnNext(updated);
    }



    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        _autoRecoveryTimer?.Dispose();
        _cancellationTokenSource.Cancel();
        _healthChangedSubject.Dispose();
    }
}