using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Authentication.Views.Hosts;
using Ecliptix.Core.Features.Splash.ViewModels;
using Ecliptix.Core.Views.Core;
using Ecliptix.Core.Features.Splash.Views;
using Splat;
using Ecliptix.Core.Core.Abstractions;
using Serilog;

namespace Ecliptix.Core;

public class ApplicationStartup(IClassicDesktopStyleApplicationLifetime desktop)
{
    private readonly IApplicationInitializer _initializer = Locator.Current.GetService<IApplicationInitializer>()!;
    private readonly IModuleManager _moduleManager = Locator.Current.GetService<IModuleManager>()!;
    private SplashWindowViewModel? _splashViewModel;
    private SplashWindow? _splashScreen;

    private const string RootContentName = "MainContentGrid";

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
        if (_splashScreen is null)
        {
            Log.Warning("TransitionToNextWindow called but _splashScreen is null");
            return;
        }

        Log.Information("Starting transition from splash to next window");
        Window nextWindow = CreateNextWindow();
        Log.Information("Next window created: {WindowType}", nextWindow.GetType().Name);

        nextWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        nextWindow.Opacity = 0;

        await ShowAndWaitForWindow(nextWindow);
        Log.Information("Next window shown and opened");

        PositionWindow(nextWindow, _splashScreen);

        desktop.MainWindow = nextWindow;
        Log.Information("Set next window as MainWindow");

        await PerformCrossfadeTransition(nextWindow);
        Log.Information("Crossfade transition completed");

        _splashScreen.Close();
        _splashScreen = null;
        Log.Information("Splash screen closed - transition complete");
    }

    private Window CreateNextWindow()
    {
        if (_initializer.IsMembershipConfirmed)
        {
            Log.Information("Creating MainHostWindow - membership confirmed");
            return new MainHostWindow();
        }

        Log.Information("Creating MembershipHostWindow - membership not confirmed");
        MembershipHostWindowModel viewModel = Locator.Current.GetService<MembershipHostWindowModel>()!;
        Log.Information("Created MembershipHostWindowModel: {ViewModelType}", viewModel.GetType().Name);
        return new MembershipHostWindow { DataContext = viewModel };
    }

    private static async Task ShowAndWaitForWindow(Window window)
    {
        TaskCompletionSource openedTcs = new();

        window.Opened += OnOpened;
        window.Show();
        await openedTcs.Task;
        return;

        void OnOpened(object? sender, EventArgs e)
        {
            window.Opened -= OnOpened;
            openedTcs.TrySetResult();
        }
    }

    private static void PositionWindow(Window nextWindow, Window splashScreen)
    {
        PixelPoint splashPos = splashScreen.Position;
        Size splashSize = splashScreen.ClientSize;
        Size nextSize = nextWindow.ClientSize;

        int centeredX = splashPos.X + (int)((splashSize.Width - nextSize.Width) / 2);
        int centeredY = splashPos.Y + (int)((splashSize.Height - nextSize.Height) / 2);
        nextWindow.Position = new PixelPoint(centeredX, centeredY);
    }

    private async Task PerformCrossfadeTransition(Window nextWindow)
    {
        const int fadeTransitionMs = 700;
        TimeSpan duration = TimeSpan.FromMilliseconds(fadeTransitionMs);

        Grid? contentToFadeIn = nextWindow.FindControl<Grid>(RootContentName);
        if (contentToFadeIn is not null)
            contentToFadeIn.Opacity = 1;

        await Task.WhenAll(
            AnimateWindowOpacity(_splashScreen, 1, 0, duration),
            AnimateWindowOpacity(nextWindow, 0, 1, duration)
        );
    }

    private static async Task AnimateWindowOpacity(Window? window, double from, double to, TimeSpan duration)
    {
        if (window == null) return;

        const int steps = 30;
        TimeSpan stepDuration = TimeSpan.FromTicks(duration.Ticks / steps);
        double stepChange = (to - from) / steps;

        for (int i = 0; i <= steps; i++)
        {
            double currentOpacity = from + (stepChange * i);
            window.Opacity = Math.Clamp(currentOpacity, 0, 1);
            await Task.Delay(stepDuration);
        }

        window.Opacity = to;
    }
}