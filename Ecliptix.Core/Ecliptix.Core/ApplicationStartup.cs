// Ecliptix.Core/Services/ApplicationStartup.cs (MODIFIED)

using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Authentication;
using Serilog;
using Splat;

namespace Ecliptix.Core;

public class ApplicationStartup(IClassicDesktopStyleApplicationLifetime desktop)
{
    private readonly IApplicationInitializer _initializer = Locator.Current.GetService<IApplicationInitializer>()!;

    private readonly SplashScreen _splashScreen = new()
    {
        DataContext = Locator.Current.GetService<SplashScreenViewModel>()!
    };

    public async Task RunAsync()
    {
        desktop.MainWindow = _splashScreen;
        _splashScreen.Show();

        try
        {
            bool success = await Task.Run(() => _initializer.InitializeAsync());
            if (success)
            {
                TransitionToNextWindow();
            }
            else
            {
                Log.Error("Application initialization failed. The application will now exit");
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "A critical unhandled exception occurred during application startup. Shutting down");
            desktop.Shutdown();
        }
        finally
        {
            _splashScreen.Close();
        }
    }

    private void TransitionToNextWindow()
    {
        Window nextWindow;
        if (!_initializer.IsMembershipConfirmed)
        {
            nextWindow = new AuthenticationWindow
            {
                DataContext = Locator.Current.GetService<AuthenticationViewModel>()
            };
        }
        else
        {
            Log.Warning("Membership confirmed, but no main application window is defined. Shutting down");
            desktop.Shutdown();
            return;
        }

        desktop.MainWindow = nextWindow;
        nextWindow.Show();
    }
}