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
        DefaultSystemSettings defaultSystemSettings = Locator.Current.GetService<DefaultSystemSettings>()!;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Initialize modules first, then start the application on UI thread
            _ = Task.Run(async () =>
            {
                await InitializeModulesAsync();
                
                Serilog.Log.Information("Modules loaded, starting ApplicationStartup...");
                
                // Switch back to UI thread for ApplicationStartup
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    Serilog.Log.Information("On UI thread, creating ApplicationStartup...");
                    await new ApplicationStartup(desktop).RunAsync(defaultSystemSettings);
                });
            });
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeModulesAsync()
    {
        IModuleManager moduleManager = Locator.Current.GetService<IModuleManager>()!;
        await moduleManager.LoadEagerModulesAsync();
    }
}