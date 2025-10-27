using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Settings;
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
            Locator.CurrentMutable.RegisterConstant(desktop, typeof(IClassicDesktopStyleApplicationLifetime));

            _ = Task.Run(async () =>
            {
                await InitializeModulesAsync();

                Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                {
                    ApplicationStartup applicationStartup = Locator.Current.GetService<ApplicationStartup>()!;
                    await applicationStartup.RunAsync(defaultSystemSettings);
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
