using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Features.Splash.ViewModels;
using Ecliptix.Core.Features.Splash.Views;
using Ecliptix.Opaque.Protocol;
using Ecliptix.Security.SSL.Native.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.SslPinning;
using Splat;
using Serilog;

namespace Ecliptix.Core;

public class ApplicationStartup
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IApplicationInitializer _initializer;
    private readonly IModuleManager _moduleManager;
    private readonly IWindowService _windowService;
    private SplashWindowViewModel? _splashViewModel;
    private SplashWindow? _splashScreen;

    public ApplicationStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        _initializer = Locator.Current.GetService<IApplicationInitializer>()!;
        _moduleManager = Locator.Current.GetService<IModuleManager>()!;
        _windowService = Locator.Current.GetService<IWindowService>()!;
    }

    public async Task RunAsync(DefaultSystemSettings defaultSystemSettings)
    {
        _splashViewModel = Locator.Current.GetService<SplashWindowViewModel>()!;
        _splashScreen = new SplashWindow { DataContext = _splashViewModel };

        _desktop.MainWindow = _splashScreen;
        _splashScreen?.Show();

        await _splashViewModel.IsSubscribed.Task;

        // Initialize SSL pinning library early in desktop startup
        Log.Information("Initializing SSL pinning library for desktop application");
        using var sslPinningService = new SslPinningService();
        Result<Unit, SslPinningFailure> sslInitResult = await sslPinningService.InitializeAsync();
        if (sslInitResult.IsErr)
        {
            Log.Error("SSL pinning library initialization failed: {Error} - desktop cannot proceed", sslInitResult.UnwrapErr());
            await _splashViewModel.PrepareForShutdownAsync();
            _desktop.Shutdown();
            return;
        }
        Log.Information("SSL pinning library initialized successfully");

        // Initialize OPAQUE client library early in desktop startup
        Log.Information("Initializing OPAQUE client library for desktop application");
        bool opaqueClientReady = OpaqueClientTest.ValidateClientIntegration();
        if (!opaqueClientReady)
        {
            Log.Error("OPAQUE client library initialization failed - desktop cannot proceed");
            await _splashViewModel.PrepareForShutdownAsync();
            _desktop.Shutdown();
            return;
        }
        Log.Information("OPAQUE client library initialized successfully");

        bool success = await _initializer.InitializeAsync(defaultSystemSettings);
        if (success)
        {
            await LoadAuthenticationModuleAsync();
            await TransitionToNextWindowAsync();
        }
        else
        {
            await _splashViewModel.PrepareForShutdownAsync();
            _desktop.Shutdown();
        }
    }

    private async Task LoadAuthenticationModuleAsync()
    {
        await _moduleManager.LoadModuleAsync(ModuleIdentifier.Authentication.ToName());
    }

    private async Task TransitionToNextWindowAsync()
    {
        if (_splashScreen is null)
        {
            Log.Warning("TransitionToNextWindow called but _splashScreen is null");
            return;
        }

        try
        {
            Window nextWindow = await _windowService.TransitionFromSplashAsync(_splashScreen, _initializer.IsMembershipConfirmed);

            _desktop.MainWindow = nextWindow;

            await _windowService.PerformCrossfadeTransitionAsync(_splashScreen, nextWindow);

            _splashScreen.Close();
            _splashScreen = null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to transition from splash screen");
            throw;
        }
    }
}