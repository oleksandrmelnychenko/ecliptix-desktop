using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.Views.Core;
using Ecliptix.Core.Views.Memberships;
using Ecliptix.Core.Views.Memberships.Components.Splash;
using Splat;

namespace Ecliptix.Core;

public class ApplicationStartup
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IApplicationInitializer _initializer;
    private readonly SplashWindowViewModel _splashViewModel;
    private SplashWindow? _splashScreen;

    private const string RootContentName = "MainContentGrid";

    public ApplicationStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        _initializer = Locator.Current.GetService<IApplicationInitializer>()!;
        _splashViewModel = Locator.Current.GetService<SplashWindowViewModel>()!;
        _splashScreen = new SplashWindow { DataContext = _splashViewModel };
    }

    public async Task RunAsync(DefaultSystemSettings defaultSystemSettings)
    {
        _desktop.MainWindow = _splashScreen;
        _splashScreen?.Show();

        await _splashViewModel.IsSubscribed.Task;

        bool success = await _initializer.InitializeAsync(defaultSystemSettings);
        if (success)
        {
            await TransitionToNextWindowAsync();
        }
        else
        {
            await _splashViewModel.PrepareForShutdownAsync();
            _desktop.Shutdown();
        }
    }

    private async Task TransitionToNextWindowAsync()
    {
        if (_splashScreen is null) return;

        Window nextWindow = CreateNextWindow();

        nextWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        nextWindow.Opacity = 0;

        await ShowAndWaitForWindow(nextWindow);

        PositionWindow(nextWindow, _splashScreen);

        _desktop.MainWindow = nextWindow;

        await PerformCrossfadeTransition(nextWindow);

        _splashScreen.Close();
        _splashScreen = null;
    }

    private Window CreateNextWindow()
    {
        if (_initializer.IsMembershipConfirmed) return new MainHostWindow();
        MembershipHostWindowModel viewModel = Locator.Current.GetService<MembershipHostWindowModel>()!;
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