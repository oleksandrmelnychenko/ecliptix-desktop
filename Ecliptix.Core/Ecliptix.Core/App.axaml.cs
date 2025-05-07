using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Memberships;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Memberships;
using Splat;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        AppSettings? appSettings = Locator.Current.GetService<AppSettings>();
        if (appSettings == null)
        {
            //TODO: load store to get the settings.
        }

        const bool isAuthorized = false;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AuthorizationViewModel authViewModel = Locator.Current.GetService<AuthorizationViewModel>()!;
            desktop.MainWindow = new AuthorizationWindow
            {
                DataContext = authViewModel
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = Locator.Current.GetService<MainViewModel>()
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}