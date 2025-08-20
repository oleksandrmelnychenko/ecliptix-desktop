using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
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

        _ = InitializeModulesAsync();

        DefaultSystemSettings defaultSystemSettings = Locator.Current.GetService<DefaultSystemSettings>()!;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _ = new ApplicationStartup(desktop).RunAsync(defaultSystemSettings);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeModulesAsync()
    {
        IModuleManager moduleManager = Locator.Current.GetService<IModuleManager>()!;
        await moduleManager.LoadEagerModulesAsync();
    }
}