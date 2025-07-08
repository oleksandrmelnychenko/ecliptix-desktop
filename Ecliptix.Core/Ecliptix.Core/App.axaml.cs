using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Views;
using ReactiveUI;
using Splat;

namespace Ecliptix.Core;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Locator.CurrentMutable.Register(() => new AppViewLocator(), typeof(IViewLocator));
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = new ApplicationStartup(desktop).RunAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
}