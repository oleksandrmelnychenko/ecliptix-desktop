using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Features.Splash.ViewModels;
using Ecliptix.Core.Features.Splash.Views;
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
        _splashViewModel = Locator.Current.GetService<SplashWindowViewModel>()!;
        _splashScreen = new SplashWindow { DataContext = _splashViewModel };

        desktop.MainWindow = _splashScreen;
        _splashScreen?.Show();

        await _splashViewModel.IsSubscribed.Task;

        Log.Information("[STARTUP] Splash screen activated, starting initialization");

        bool success = await initializer.InitializeAsync(defaultSystemSettings);

        Log.Information("[STARTUP] Initialization completed. Success: {Success}", success);

        if (success && _splashScreen != null)
        {
            bool isAuthenticated = stateManager.CurrentState == ApplicationState.Authenticated;
            Log.Information("[STARTUP] Application state: {State}, IsAuthenticated: {IsAuthenticated}",
                stateManager.CurrentState, isAuthenticated);

            Log.Information("[STARTUP] Starting transition from splash to {Target}",
                isAuthenticated ? "Main" : "Authentication");

            await router.TransitionFromSplashAsync(_splashScreen, isAuthenticated);

            Log.Information("[STARTUP] Transition completed successfully");

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