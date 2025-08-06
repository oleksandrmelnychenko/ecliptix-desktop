using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Settings;
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
        DefaultSystemSettings defaultSystemSettings = Locator.Current.GetService<DefaultSystemSettings>()!;
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = new ApplicationStartup(desktop).RunAsync(defaultSystemSettings);
        }

        base.OnFrameworkInitializationCompleted();
    }
}