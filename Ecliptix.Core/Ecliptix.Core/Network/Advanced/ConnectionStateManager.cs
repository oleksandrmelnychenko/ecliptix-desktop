using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.AppEvents.Network;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using Serilog;

namespace Ecliptix.Core.Network.Advanced;

public class ConnectionStateManager : IDisposable
{
    private readonly ConnectionStateConfiguration _config;
    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<uint, ConnectionHealth> _connections = new();
    private readonly Subject<ConnectionHealth> _healthChangedSubject = new();
    private readonly Timer _healthCheckTimer;
    private readonly Timer? _autoRecoveryTimer;
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    public IObservable<ConnectionHealth> HealthChanged => _healthChangedSubject.AsObservable();

    public ConnectionStateManager(ConnectionStateConfiguration config, ILogger? logger = null)
    {
        _config = config;
        _logger = logger ?? Log.Logger;

        _healthCheckTimer = new Timer(PerformHealthCheck, null, 
            _config.HealthCheckInterval, _config.HealthCheckInterval);

        if (_config.AutoRecoveryEnabled)
        {
            _autoRecoveryTimer = new Timer(PerformAutoRecovery, null,
                _config.AutoRecoveryInterval, _config.AutoRecoveryInterval);
        }

        _logger.Information("Advanced ConnectionStateManager initialized");
    }

    public void RegisterConnection(uint connectId)
    {
        ConnectionHealth health = new()
        {
            ConnectId = connectId,
            Status = ConnectionHealthStatus.Unknown
        };

        _connections.TryAdd(connectId, health);
        _logger.Debug("Registered connection {ConnectId} for advanced monitoring", connectId);
    }

    public void UpdateConnectionHealth(uint connectId, OperationType operation, 
        Result<Unit, NetworkFailure> result, TimeSpan latency = default)
    {
        if (!_connections.TryGetValue(connectId, out ConnectionHealth? currentHealth))
        {
            RegisterConnection(connectId);
            currentHealth = _connections[connectId];
        }

        ConnectionHealth updatedHealth = UpdateHealthMetrics(currentHealth, operation, result, latency);
        _connections.TryUpdate(connectId, updatedHealth, currentHealth);

        if (updatedHealth.Status != currentHealth.Status)
        {
            _logger.Debug("Connection {ConnectId} status changed: {OldStatus} -> {NewStatus}",
                connectId, currentHealth.Status, updatedHealth.Status);
            _healthChangedSubject.OnNext(updatedHealth);
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

    private FailureCategory CategorizeFailure(NetworkFailure failure)
    {
        string message = failure.Message.ToLowerInvariant();

        return message switch
        {
            string msg when msg.Contains("network") || msg.Contains("connection") || msg.Contains("unreachable") 
                => FailureCategory.NetworkConnectivity,
            string msg when msg.Contains("timeout") || msg.Contains("deadline") 
                => FailureCategory.Timeout,
            string msg when msg.Contains("rate") || msg.Contains("limit") || msg.Contains("throttl") 
                => FailureCategory.RateLimit,
            string msg when msg.Contains("auth") || msg.Contains("unauthorized") || msg.Contains("forbidden") 
                => FailureCategory.Authentication,
            string msg when msg.Contains("server") || msg.Contains("internal") || msg.Contains("500") 
                => FailureCategory.ServerError,
            string msg when msg.Contains("protocol") || msg.Contains("invalid") 
                => FailureCategory.Protocol,
            string msg when msg.Contains("desync") || msg.Contains("decrypt") || msg.Contains("rekey")
                => FailureCategory.CryptographicDesync,
            _ => FailureCategory.Unknown
        };
    }

    private TimeSpan CalculateAverageLatency(TimeSpan current, TimeSpan newLatency)
    {
        if (current == TimeSpan.Zero)
            return newLatency;

        const double alpha = 0.2;
        double currentMs = current.TotalMilliseconds;
        double newMs = newLatency.TotalMilliseconds;
        double averageMs = (alpha * newMs) + ((1 - alpha) * currentMs);
        
        return TimeSpan.FromMilliseconds(averageMs);
    }

    private double CalculateSuccessRate(ConnectionHealthMetrics metrics, bool wasSuccess)
    {
        const double alpha = 0.1;
        double currentRate = metrics.SuccessRate;
        double newValue = wasSuccess ? 1.0 : 0.0;
        
        return (alpha * newValue) + ((1 - alpha) * currentRate);
    }

    public ConnectionHealth? GetConnectionHealth(uint connectId)
    {
        return _connections.TryGetValue(connectId, out ConnectionHealth? health) ? health : null;
    }

    public IEnumerable<ConnectionHealth> GetAllConnectionHealth()
    {
        return _connections.Values.ToList();
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
        _logger.Debug("Removed connection {ConnectId} from advanced monitoring", connectId);
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
                    
                    _logger.Warning("Connection {ConnectId} marked as disconnected due to staleness", 
                        connection.ConnectId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during advanced health check");
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
                _logger.Information("Initiating auto-recovery for unhealthy connection {ConnectId}", 
                    connection.ConnectId);
                
                MarkConnectionRecovering(connection.ConnectId);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error during auto-recovery check");
        }
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        _autoRecoveryTimer?.Dispose();
        _cancellationTokenSource.Cancel();
        _healthChangedSubject.Dispose();
    }
}