using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Modularity.Splash;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Network.Infrastructure.Network.Core.Connectivity;
using Splat;

namespace Ecliptix.Core;

public class ApplicationStartup(
    IClassicDesktopStyleApplicationLifetime desktop,
    IApplicationInitializer initializer,
    IApplicationRouter router,
    IApplicationStateManager stateManager,
    ISplashHostFactory splashHostFactory)
{
    private ISplashHost? _splashHost;

    public async Task RunAsync(DefaultSystemSettings defaultSystemSettings)
    {
        _ = Locator.Current.GetService<InternetConnectivityBridge>();

        _splashHost = splashHostFactory.Create();
        Window splashScreen = _splashHost.CreateWindow();

        desktop.MainWindow = splashScreen;
        splashScreen.Show();

        await _splashHost.IsSubscribedAsync;

        ApplicationInitializationResult initResult = await initializer.InitializeAsync(defaultSystemSettings);

        if (initResult == ApplicationInitializationResult.SUCCESS)
        {
            bool isAuthenticated = stateManager.CurrentState == ApplicationState.AUTHENTICATED;

            await router.TransitionFromSplashAsync(splashScreen, isAuthenticated);

            _splashHost?.Dispose();
            _splashHost = null;
        }
        else
        {
            await _splashHost.PrepareForShutdownAsync();
            desktop.Shutdown();
        }
    }
}
