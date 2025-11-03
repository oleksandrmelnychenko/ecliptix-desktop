using System;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Settings;
using ReactiveUI;
using Serilog;
using Splat;

namespace Ecliptix.Core;

public class App : Application
{
    public override void Initialize()
    {
        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
        {
            var context = new
            {
                ExceptionType = ex.GetType().Name,
                InnerException = ex.InnerException?.GetType().Name,
                Timestamp = DateTime.UtcNow
            };


            Log.Error(ex, "ReactiveUI Unhandled Exception: {@Context}", context);
        });
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        DefaultSystemSettings defaultSystemSettings = Locator.Current.GetService<DefaultSystemSettings>()!;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Locator.CurrentMutable.RegisterConstant(desktop, typeof(IClassicDesktopStyleApplicationLifetime));

            Task.Run(async () =>
            {
                try
                {
                    await InitializeModulesAsync();

                    Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            ApplicationStartup applicationStartup = Locator.Current.GetService<ApplicationStartup>()!;
                            await applicationStartup.RunAsync(defaultSystemSettings);
                        }
                        catch (Exception ex)
                        {
                            Serilog.Log.Fatal(ex, "[APP-STARTUP] Critical failure during application startup execution");
                            Environment.Exit(1);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Serilog.Log.Fatal(ex, "[APP-STARTUP] Critical failure during module initialization");
                    Environment.Exit(1);
                }
            }).ContinueWith(
                task =>
                {
                    if (task.IsFaulted && task.Exception != null)
                    {
                        Serilog.Log.Fatal(task.Exception, "[APP-STARTUP] Unhandled exception in application initialization");
                        Environment.Exit(1);
                    }
                },
                TaskScheduler.Default);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task InitializeModulesAsync()
    {
        IModuleManager moduleManager = Locator.Current.GetService<IModuleManager>()!;
        await moduleManager.LoadEagerModulesAsync();
    }
}
