using System;

namespace Ecliptix.Core.Services.Abstractions.Core;

/// <summary>
/// Service for activating and bringing application windows to foreground
/// </summary>
public interface IWindowActivationService
{
    /// <summary>
    /// Brings the main application window to foreground and activates it
    /// </summary>
    void ActivateMainWindow();

    /// <summary>
    /// Event triggered when the window should be activated
    /// </summary>
    event EventHandler? WindowActivationRequested;

    /// <summary>
    /// Requests activation of the main window
    /// </summary>
    void RequestActivation();
}