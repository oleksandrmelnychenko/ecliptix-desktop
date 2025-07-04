using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.Views;
using Ecliptix.Core.Views.Authentication;
using Serilog;
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
        base.OnFrameworkInitializationCompleted();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splashVM = new SplashScreenViewModel();
            var splash = new SplashScreen { DataContext = splashVM };
            desktop.MainWindow = splash;
            splash.Show();

            // Schedule the async startup after splash is shown
            Dispatcher.UIThread.Post(async () =>
            {
                IApplicationInitializer? initializer = Locator.Current.GetService<IApplicationInitializer>();
                if (initializer is null)
                {
                    Log.Error("Application Initializer service is not registered. Shutting down");
                    ShutdownApplication(desktop, "Critical service missing.");
                    return;
                }

                splashVM.Status = "Initializing application...";

                // Run initialization on a background thread, marshal status updates to UI thread
                bool success = await Task.Run(() =>
                    initializer.InitializeAsync(status =>
                        Dispatcher.UIThread.Post(() => splashVM.Status = status)
                    )
                );

                if (success)
                {
                    splashVM.Status = "Initialization complete!";
                    var authWindow = new AuthenticationWindow
                    {
                        DataContext = Locator.Current.GetService<AuthenticationViewModel>()
                    };
                    desktop.MainWindow = authWindow;
                    authWindow.Show();
                    splash.Close();
                }
                else
                {
                    splashVM.Status = "Initialization failed. Exiting...";
                    Log.Error("Application initialization failed. The application will now exit");
                    await Task.Delay(2000); // Let user see the error
                    ShutdownApplication(desktop, "Initialization failed.");
                }
            }, DispatcherPriority.Background);
        }
    }

    

    private void ShutdownApplication(IClassicDesktopStyleApplicationLifetime desktop, string reason)
    {
        Log.Error("Shutting down application: {Reason}", reason);
        desktop.Shutdown();
    }
}