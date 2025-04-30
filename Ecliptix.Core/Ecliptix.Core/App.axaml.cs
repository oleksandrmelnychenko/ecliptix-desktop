using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.Views;
using Splat;

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
       
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = Locator.Current.GetService<MainViewModel>()
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