using System;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Serilog;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Services.Core;

/// <summary>
/// Cross-platform service for activating and bringing windows to foreground
/// </summary>
public class WindowActivationService : IWindowActivationService
{

    public event EventHandler? WindowActivationRequested;

    public WindowActivationService()
    {
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
                    Log.Information("Attempting to activate main window");

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

                    Log.Information("Main window activation completed");
                }
                else
                {
                    Log.Warning("Main window is null, cannot activate");
                }
            }
            else
            {
                Log.Warning("Application lifetime is not desktop style, cannot activate window");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to activate main window");
        }
    }

    public void RequestActivation()
    {
        Log.Debug("Window activation requested");
        WindowActivationRequested?.Invoke(this, EventArgs.Empty);
    }
}