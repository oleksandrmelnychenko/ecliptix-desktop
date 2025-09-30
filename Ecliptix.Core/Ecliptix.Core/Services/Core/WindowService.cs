using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Authentication.Views.Hosts;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Views.Core;
using Splat;

namespace Ecliptix.Core.Services.Core;

public class WindowService : IWindowService
{
    private const string DefaultRootContentName = "MainContentGrid";
    private const int CrossfadeTransitionMs = 700;
    private const int TargetFrameDelayMs = 16;

    public async Task<Window> TransitionFromSplashAsync(Window splashWindow, bool isMembershipConfirmed)
    {
        Window nextWindow = CreateNextWindow(isMembershipConfirmed);

        nextWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        nextWindow.Opacity = 0;

        await ShowAndWaitForWindowAsync(nextWindow);
        PositionWindowRelativeTo(nextWindow, splashWindow);

        return nextWindow;
    }

    public async Task AnimateWindowOpacityAsync(Window window, double from, double to, TimeSpan duration)
    {
        await AnimateWindowOpacityAsync(window, from, to, duration, CancellationToken.None);
    }

    public async Task AnimateWindowOpacityAsync(
        Window window,
        double from,
        double to,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        DateTime startTime = DateTime.UtcNow;

        while (!cancellationToken.IsCancellationRequested)
        {
            TimeSpan elapsed = DateTime.UtcNow - startTime;
            double progress = Math.Clamp(elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);

            if (progress >= 1)
                break;

            double easedProgress = EaseInOutCubic(progress);
            window.Opacity = Math.Clamp(from + ((to - from) * easedProgress), 0, 1);

            await Task.Delay(TargetFrameDelayMs, cancellationToken);
        }

        window.Opacity = to;
    }

    public async Task ShowAndWaitForWindowAsync(Window window)
    {
        TaskCompletionSource openedTcs = new();
        EventHandler handler = null!;

        handler = (sender, e) =>
        {
            window.Opened -= handler;
            openedTcs.TrySetResult();
        };

        window.Opened += handler;
        window.Show();
        await openedTcs.Task;
    }

    public void PositionWindowRelativeTo(Window targetWindow, Window referenceWindow)
    {
        PixelPoint referencePos = referenceWindow.Position;
        Size referenceSize = referenceWindow.ClientSize;
        Size targetSize = targetWindow.ClientSize;

        int centeredX = referencePos.X + (int)((referenceSize.Width - targetSize.Width) / 2);
        int centeredY = referencePos.Y + (int)((referenceSize.Height - targetSize.Height) / 2);

        targetWindow.Position = new PixelPoint(centeredX, centeredY);
    }

    public async Task PerformCrossfadeTransitionAsync(Window fromWindow, Window toWindow, string? contentGridName = null)
    {
        TimeSpan duration = TimeSpan.FromMilliseconds(CrossfadeTransitionMs);

        string gridName = contentGridName ?? DefaultRootContentName;
        Grid? contentToFadeIn = toWindow.FindControl<Grid>(gridName);
        if (contentToFadeIn != null)
            contentToFadeIn.Opacity = 1;

        await Task.WhenAll(
            AnimateWindowOpacityAsync(fromWindow, 1, 0, duration),
            AnimateWindowOpacityAsync(toWindow, 0, 1, duration)
        );
    }

    private static double EaseInOutCubic(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow(-2 * t + 2, 3) / 2);

    private Window CreateNextWindow(bool isMembershipConfirmed)
    {
        if (isMembershipConfirmed)
        {
            return new MainHostWindow();
        }

        MembershipHostWindowModel viewModel = Locator.Current.GetService<MembershipHostWindowModel>()!;
        return new MembershipHostWindow { DataContext = viewModel };
    }
}