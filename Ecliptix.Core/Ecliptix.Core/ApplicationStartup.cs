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

public class ApplicationStartup
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IApplicationInitializer _initializer;
    private readonly SplashScreenViewModel _splashViewModel;
    private readonly SplashScreen _splashScreen;

    public ApplicationStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        _initializer = Locator.Current.GetService<IApplicationInitializer>()!;
        _splashViewModel = Locator.Current.GetService<SplashScreenViewModel>()!;
        _splashScreen = new SplashScreen { DataContext = _splashViewModel };
    }

    public async Task RunAsync()
    {
        _desktop.MainWindow = _splashScreen;
        _splashScreen.Show();

        try
        {
            await _splashViewModel.IsSubscribed.Task;

            bool success = await Task.Run(() => _initializer.InitializeAsync());
            if (success)
            {
                TransitionToNextWindow();
            }
            else
            {
                Log.Error("Application initialization failed. The application will now exit");
                _desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "A critical unhandled exception occurred during application startup. Shutting down");
            _desktop.Shutdown();
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
            _desktop.Shutdown();
            return;
        }

        _desktop.MainWindow = nextWindow;
        nextWindow.Show();
    }
}