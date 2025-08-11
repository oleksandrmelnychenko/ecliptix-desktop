using System;
using Ecliptix.Core.Network.Contracts.Services;
using Ecliptix.Core.Network.Core;

namespace Ecliptix.Core.Network.Contracts.Core;

/// <summary>
/// Comprehensive diagnostics information for a network connection combining health status and retry metrics
/// </summary>
public record ConnectionDiagnostics(
    /// <summary>
    /// The connection identifier
    /// </summary>
    uint ConnectId,
    
    /// <summary>
    /// Current connection health information including status and metrics
    /// </summary>
    ConnectionHealth Health,
    
    /// <summary>
    /// Retry strategy metrics including success/failure counts and timing
    /// </summary>
    RetryMetrics RetryMetrics,
    
    /// <summary>
    /// Current retry state including circuit breaker status and consecutive failures
    /// </summary>
    ConnectionRetryState? RetryState,
    
    /// <summary>
    /// Whether the connection is currently in an outage state
    /// </summary>
    bool IsInOutage
)
{
    /// <summary>
    /// Gets a human-readable summary of the connection status
    /// </summary>
    public string StatusSummary => $"Health: {Health.Status}, " +
                                  $"Success Rate: {RetryMetrics.SuccessfulAttempts}/{RetryMetrics.TotalAttempts}, " +
                                  $"Circuit: {(RetryState?.IsCircuitOpen == true ? "Open" : "Closed")}, " +
                                  $"Outage: {(IsInOutage ? "Yes" : "No")}";
    
    /// <summary>
    /// Gets the overall connection health score as a percentage (0-100)
    /// </summary>
    public double HealthScore
    {
        get
        {
            if (IsInOutage) return 0.0;
            if (RetryState?.IsCircuitOpen == true) return 10.0;
            
            double baseScore = Health.Status switch
            {
                ConnectionHealthStatus.Healthy => 100.0,
                ConnectionHealthStatus.Degraded => 70.0,
                ConnectionHealthStatus.Unhealthy => 30.0,
                ConnectionHealthStatus.Failed => 10.0,
                _ => 50.0
            };
            
            // Adjust based on retry success rate
            if (RetryMetrics.TotalAttempts > 0)
            {
                double successRate = (double)RetryMetrics.SuccessfulAttempts / RetryMetrics.TotalAttempts;
                baseScore *= successRate;
            }
            
            return Math.Max(0.0, Math.Min(100.0, baseScore));
        }
    }
}