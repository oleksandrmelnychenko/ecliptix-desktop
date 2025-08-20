using System;

namespace Ecliptix.Core.Services.Abstractions.Core;

/// <summary>
/// Manages single instance application behavior across different platforms
/// </summary>
public interface ISingleInstanceManager : IDisposable
{
    /// <summary>
    /// Attempts to acquire the single instance lock
    /// </summary>
    /// <returns>True if this is the first instance, false if another instance is already running</returns>
    bool TryAcquireInstance();

    /// <summary>
    /// Signals an existing instance to bring itself to foreground
    /// </summary>
    /// <returns>True if signal was sent successfully</returns>
    bool NotifyExistingInstance();

    /// <summary>
    /// Event triggered when another instance tries to start and signals this instance
    /// </summary>
    event EventHandler? InstanceActivationRequested;
}