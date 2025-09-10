using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Ecliptix.Core.Features.Authentication.Views.Hosts;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Views.Core;
using Microsoft.Extensions.Logging;
using Splat;

namespace Ecliptix.Core.Services.Core
{
    public class WindowService : IWindowService
    {
        private readonly ILogger<WindowService> _logger;
        private const string DefaultRootContentName = "MainContentGrid";

        public WindowService(ILogger<WindowService> logger)
        {
            _logger = logger;
        }

        public async Task<Window> TransitionFromSplashAsync(Window splashWindow, bool isMembershipConfirmed)
        {
            if (splashWindow == null)
            {
                _logger.LogWarning("TransitionFromSplash called but splashWindow is null");
                throw new ArgumentNullException(nameof(splashWindow));
            }

            _logger.LogInformation("Starting transition from splash to next window");
            
            Window nextWindow = CreateNextWindow(isMembershipConfirmed);
            
            nextWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            nextWindow.Opacity = 0;

            await ShowAndWaitForWindowAsync(nextWindow);
            PositionWindowRelativeTo(nextWindow, splashWindow);
            
            return nextWindow;
        }

        public async Task AnimateWindowOpacityAsync(Window window, double from, double to, TimeSpan duration)
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

        public async Task ShowAndWaitForWindowAsync(Window window)
        {
            TaskCompletionSource openedTcs = new();

            window.Opened += OnOpened;
            window.Show();
            await openedTcs.Task;
            
            void OnOpened(object? sender, EventArgs e)
            {
                window.Opened -= OnOpened;
                openedTcs.TrySetResult();
            }
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
            const int fadeTransitionMs = 700;
            TimeSpan duration = TimeSpan.FromMilliseconds(fadeTransitionMs);

            string gridName = contentGridName ?? DefaultRootContentName;
            Grid? contentToFadeIn = toWindow.FindControl<Grid>(gridName);
            if (contentToFadeIn != null)
                contentToFadeIn.Opacity = 1;

            await Task.WhenAll(
                AnimateWindowOpacityAsync(fromWindow, 1, 0, duration),
                AnimateWindowOpacityAsync(toWindow, 0, 1, duration)
            );
        }

        private Window CreateNextWindow(bool isMembershipConfirmed)
        {
            if (isMembershipConfirmed)
            {
                _logger.LogInformation("Creating MainHostWindow - membership confirmed");
                return new MainHostWindow();
            }

            _logger.LogInformation("Creating MembershipHostWindow - membership not confirmed");
            MembershipHostWindowModel viewModel = Locator.Current.GetService<MembershipHostWindowModel>()!;
            return new MembershipHostWindow { DataContext = viewModel };
        }
    }
}