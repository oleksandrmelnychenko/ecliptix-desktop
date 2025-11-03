using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Features.Splash.ViewModels;
using Ecliptix.Core.Features.Splash.Views;
using Ecliptix.Core.Infrastructure.Network.Core.Connectivity;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Splat;

namespace Ecliptix.Core;

public class ApplicationStartup(
    IClassicDesktopStyleApplicationLifetime desktop,
    IApplicationInitializer initializer,
    IApplicationRouter router,
    IApplicationStateManager stateManager)
{
    private SplashWindowViewModel? _splashViewModel;

    public async Task RunAsync(DefaultSystemSettings defaultSystemSettings)
    {
        _ = Locator.Current.GetService<InternetConnectivityBridge>();

        _splashViewModel = Locator.Current.GetService<SplashWindowViewModel>()!;
        SplashWindow splashScreen = new() { DataContext = _splashViewModel };

        desktop.MainWindow = splashScreen;
        splashScreen.Show();

        await _splashViewModel.IsSubscribed.Task;

        bool success = await initializer.InitializeAsync(defaultSystemSettings);

        if (success)
        {
            bool isAuthenticated = stateManager.CurrentState == ApplicationState.Authenticated;

            await router.TransitionFromSplashAsync(splashScreen, isAuthenticated);

            _splashViewModel?.Dispose();
            _splashViewModel = null;
        }
        else
        {
            await _splashViewModel.PrepareForShutdownAsync();
            desktop.Shutdown();
        }
    }
}
