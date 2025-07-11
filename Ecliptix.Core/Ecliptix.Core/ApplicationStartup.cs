using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.Views.Memberships;
using Ecliptix.Core.Views.Memberships.Components.Splash;
using Serilog;
using Splat;

namespace Ecliptix.Core;

public class ApplicationStartup
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IApplicationInitializer _initializer;
    private readonly SplashWindowViewModel _splashViewModel;
    private readonly SplashWindow _splashScreen;

    public ApplicationStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        _initializer = Locator.Current.GetService<IApplicationInitializer>()!;
        _splashViewModel = Locator.Current.GetService<SplashWindowViewModel>()!;
        _splashScreen = new SplashWindow { DataContext = _splashViewModel };
    }

    public async Task RunAsync()
    {
        _desktop.MainWindow = _splashScreen;
        _splashScreen.Show();

        await _splashViewModel.IsSubscribed.Task;

        bool success = await _initializer.InitializeAsync();
        if (success)
        {
            TransitionToNextWindow();
        }
        else
        {
           await _splashViewModel.PrepareForShutdownAsync();
           _desktop.Shutdown();
        }
    }

    private void TransitionToNextWindow()
    {
        Window nextWindow;
        if (!_initializer.IsMembershipConfirmed)
        {
            _splashScreen.Hide();
            nextWindow = new MembershipHostWindow
            {
                DataContext = Locator.Current.GetService<MembershipHostWindowModel>()
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