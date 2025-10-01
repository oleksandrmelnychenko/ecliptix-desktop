using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Features.Splash.ViewModels;
using Ecliptix.Core.Features.Splash.Views;
using Splat;
using Serilog;

namespace Ecliptix.Core;

public class ApplicationStartup(IClassicDesktopStyleApplicationLifetime desktop)
{
    private readonly IApplicationInitializer _initializer = Locator.Current.GetService<IApplicationInitializer>()!;
    private readonly IModuleManager _moduleManager = Locator.Current.GetService<IModuleManager>()!;
    private readonly IWindowService _windowService = Locator.Current.GetService<IWindowService>()!;

    private SplashWindowViewModel? _splashViewModel;
    private SplashWindow? _splashScreen;

    public async Task RunAsync(DefaultSystemSettings defaultSystemSettings)
    {
        _splashViewModel = Locator.Current.GetService<SplashWindowViewModel>()!;
        _splashScreen = new SplashWindow { DataContext = _splashViewModel };

        desktop.MainWindow = _splashScreen;
        _splashScreen?.Show();

        await _splashViewModel.IsSubscribed.Task;

        bool success = await _initializer.InitializeAsync(defaultSystemSettings);
        if (success)
        {
            await LoadAuthenticationModuleAsync();
            await TransitionToNextWindowAsync();
        }
        else
        {
            await _splashViewModel.PrepareForShutdownAsync();
            desktop.Shutdown();
        }
    }

    private async Task LoadAuthenticationModuleAsync()
    {
        await _moduleManager.LoadModuleAsync(ModuleIdentifier.Authentication.ToName());
    }

    private async Task TransitionToNextWindowAsync()
    {
        Window nextWindow =
            await _windowService.TransitionFromSplashAsync(_splashScreen, _initializer.IsMembershipConfirmed);

        desktop.MainWindow = nextWindow;

        await _windowService.PerformCrossfadeTransitionAsync(_splashScreen, nextWindow);

        _splashScreen.Close();
        _splashViewModel?.Dispose();
        _splashScreen = null;
        _splashViewModel = null;
    }
}