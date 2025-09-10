using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Ecliptix.Core.Services.Abstractions.Core
{
    public interface IWindowService
    {
        Task<Window> TransitionFromSplashAsync(Window splashWindow, bool isMembershipConfirmed);
        Task AnimateWindowOpacityAsync(Window window, double from, double to, TimeSpan duration);
        Task ShowAndWaitForWindowAsync(Window window);
        void PositionWindowRelativeTo(Window targetWindow, Window referenceWindow);
        Task PerformCrossfadeTransitionAsync(Window fromWindow, Window toWindow, string? contentGridName = null);
    }
}