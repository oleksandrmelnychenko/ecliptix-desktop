using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Features.Splash.ViewModels;
using Ecliptix.Core.Features.Splash.Views;
using Ecliptix.Core.Infrastructure.Network.Core.Connectivity;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Serilog;
using Splat;

namespace Ecliptix.Core;

public class ApplicationStartup(
    IClassicDesktopStyleApplicationLifetime desktop,
    IApplicationInitializer initializer,
    IApplicationRouter router,
    IApplicationStateManager stateManager)
{
    private SplashWindowViewModel? _splashViewModel;
    private SplashWindow? _splashScreen;

    public async Task RunAsync(DefaultSystemSettings defaultSystemSettings)
    {
        _ = Locator.Current.GetService<InternetConnectivityBridge>();

        _splashViewModel = Locator.Current.GetService<SplashWindowViewModel>()!;
        _splashScreen = new SplashWindow { DataContext = _splashViewModel };

        desktop.MainWindow = _splashScreen;
        _splashScreen?.Show();

        await _splashViewModel.IsSubscribed.Task;

        bool success = await initializer.InitializeAsync(defaultSystemSettings);

        if (success && _splashScreen != null)
        {
            bool isAuthenticated = stateManager.CurrentState == ApplicationState.Authenticated;

            await router.TransitionFromSplashAsync(_splashScreen, isAuthenticated);

            _splashViewModel?.Dispose();
            _splashViewModel = null;
            _splashScreen = null;
        }
        else
        {
            Log.Warning("[STARTUP] Initialization failed or splash screen is null, shutting down");
            await _splashViewModel.PrepareForShutdownAsync();
            desktop.Shutdown();
        }
    }
}
