using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Core;
using Ecliptix.Core.Views.Memberships;
using Ecliptix.Core.Views.Memberships.Components.Splash;
using Serilog;
using Splat;

namespace Ecliptix.Core;

public class ApplicationStartup
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IApplicationInitializer _initializer;
    private SplashWindowViewModel? _splashViewModel;
    private SplashWindow? _splashScreen;

    private const string RootContentName = "MainContentGrid";

    public ApplicationStartup(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        _initializer = Locator.Current.GetService<IApplicationInitializer>()!;
        _splashViewModel = Locator.Current.GetService<SplashWindowViewModel>()!;
        _splashScreen = new SplashWindow { DataContext = _splashViewModel };
    }

    public async Task RunAsync()
    {
        _desktop.MainWindow = _splashScreen;
        _splashScreen?.Show();

        await _splashViewModel?.IsSubscribed.Task!;

        bool success = await _initializer.InitializeAsync();
        if (success)
        {
            await TransitionToNextWindowAsync();
        }
        else
        {
            if (_splashViewModel != null)
            {
                await _splashViewModel.PrepareForShutdownAsync();
            }

            _desktop.Shutdown();
        }
    }

    private async Task TransitionToNextWindowAsync()
    {
        if (_splashScreen is null) return;

        Window nextWindow;
        if (!_initializer.IsMembershipConfirmed)
        {
            nextWindow = new MembershipHostWindow
            {
                DataContext = Locator.Current.GetService<MembershipHostWindowModel>()
            };
        }
        else
        {
            nextWindow = new MainHostWindow
            {
            };
        }

        nextWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        PixelPoint splashPos = _splashScreen.Position;
        Size splashSize = _splashScreen.ClientSize;

        const int n = 2;
        
        int centeredX = splashPos.X + (int)((splashSize.Width - nextWindow.Width) / n);
        int centeredY = splashPos.Y + (int)((splashSize.Height - nextWindow.Height) / n);
        nextWindow.Position = new PixelPoint(centeredX, centeredY);

        Grid? contentToFadeIn = nextWindow.FindControl<Grid>(RootContentName);

        nextWindow.Show();
        _desktop.MainWindow = nextWindow;

        if (_splashScreen.Content is Grid splashContentToFadeOut && contentToFadeIn != null)
        {
            TimeSpan duration = TimeSpan.FromMilliseconds(700);

            Task fadeOutTask = splashContentToFadeOut.FadeOutAsync(duration);
            Task fadeInTask = contentToFadeIn.FadeInAsync(duration);
            await Task.WhenAll(fadeOutTask, fadeInTask);
        }
        else
        {
            if (contentToFadeIn != null) contentToFadeIn.Opacity = 1;
        }

        _splashScreen.Close();

        _splashScreen = null;
        _splashViewModel = null;
    }
}