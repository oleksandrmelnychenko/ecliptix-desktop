using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Services.Core;

/// <summary>
/// Cross-platform service for activating and bringing windows to foreground
/// </summary>
public class WindowActivationService : IWindowActivationService
{
    private readonly ILogger<WindowActivationService> _logger;
    
    public event EventHandler? WindowActivationRequested;
    
    public WindowActivationService(ILogger<WindowActivationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public void ActivateMainWindow()
    {
        try
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Window? mainWindow = desktop.MainWindow;
                if (mainWindow != null)
                {
                    _logger.LogInformation("Attempting to activate main window");
                    
                    // Restore window if minimized
                    if (mainWindow.WindowState == WindowState.Minimized)
                    {
                        mainWindow.WindowState = WindowState.Normal;
                    }
                    
                    // Bring to front and activate
                    mainWindow.Activate();
                    mainWindow.Topmost = true;
                    mainWindow.Topmost = false; // Reset topmost to allow normal z-order behavior
                    mainWindow.Focus();
                    
                    _logger.LogInformation("Main window activation completed");
                }
                else
                {
                    _logger.LogWarning("Main window is null, cannot activate");
                }
            }
            else
            {
                _logger.LogWarning("Application lifetime is not desktop style, cannot activate window");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate main window");
        }
    }
    
    public void RequestActivation()
    {
        _logger.LogDebug("Window activation requested");
        WindowActivationRequested?.Invoke(this, EventArgs.Empty);
    }
}