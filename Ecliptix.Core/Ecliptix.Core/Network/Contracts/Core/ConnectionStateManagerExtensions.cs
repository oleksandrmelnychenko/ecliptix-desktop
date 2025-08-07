using System;
using System.Collections.Generic;
using Ecliptix.Core.Network.Advanced;
using Ecliptix.Core.Network.Core;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.Contracts.Core;

public static class ConnectionStateManagerExtensions
{
    public static void UpdateConnectionHealth(
        this IConnectionStateManager connectionStateManager,
        uint connectId, 
        Result<Unit, NetworkFailure> result)
    {
        ConnectionHealth? currentHealth = connectionStateManager.GetConnectionHealth(connectId);
        
        if (currentHealth == null)
        {
            ConnectionHealth defaultHealth = new()
            {
                ConnectId = connectId,
                Status = ConnectionHealthStatus.Unknown,
                LastHealthCheck = DateTime.UtcNow
            };
            connectionStateManager.RegisterConnection(connectId, defaultHealth);
            currentHealth = defaultHealth;
        }

        ConnectionHealthStatus newStatus = DetermineHealthStatus(currentHealth, result);
        NetworkFailure? failure = result.IsErr ? result.UnwrapErr() : null;
        
        connectionStateManager.UpdateConnectionHealth(connectId, newStatus, failure);
    }
    
    private static ConnectionHealthStatus DetermineHealthStatus(
        ConnectionHealth current, 
        Result<Unit, NetworkFailure> result)
    {
        ConnectionHealthMetrics metrics = current.Metrics;
        
        if (result.IsOk)
        {
            double newSuccessRate = CalculateSuccessRate(metrics.SuccessRate, true);
            
            if (newSuccessRate >= 0.95 && metrics.ConsecutiveFailures == 0)
                return ConnectionHealthStatus.Healthy;
            
            if (newSuccessRate >= 0.8)
                return ConnectionHealthStatus.Degraded;
        }
        else
        {
            int newFailures = metrics.ConsecutiveFailures + 1;
            double newSuccessRate = CalculateSuccessRate(metrics.SuccessRate, false);
            NetworkFailure failure = result.UnwrapErr();
            
            if (IsCriticalFailure(failure) || newFailures >= 5)
                return ConnectionHealthStatus.Failed;
            
            if (newSuccessRate < 0.5 || newFailures >= 3)
                return ConnectionHealthStatus.Unhealthy;
            
            if (newFailures > 0 || newSuccessRate < 0.95)
                return ConnectionHealthStatus.Degraded;
        }
        
        return current.Status;
    }
    
    private static bool IsCriticalFailure(NetworkFailure failure)
    {
        string message = failure.Message.ToLowerInvariant();
        return message.Contains("desync") || 
               message.Contains("unauthorized") || 
               message.Contains("forbidden") ||
               message.Contains("protocol");
    }
    
    private static double CalculateSuccessRate(double currentRate, bool wasSuccess)
    {
        const double alpha = 0.1;
        double newValue = wasSuccess ? 1.0 : 0.0;
        return (alpha * newValue) + ((1 - alpha) * currentRate);
    }
}