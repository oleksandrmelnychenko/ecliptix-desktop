using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;
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

        _ = StartupAsync();
    }

    private async Task StartupAsync()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        IApplicationInitializer? initializer = Locator.Current.GetService<IApplicationInitializer>();
        if (initializer is null)
        {
            Log.Error("Application Initializer service is not registered. Shutting down");
            ShutdownApplication(desktop, "Critical service missing.");
            return;
        }

        bool success = await initializer.InitializeAsync();
        if (success)
        {
            if (!initializer.IsMembershipConfirmed)
            {
                desktop.MainWindow = new AuthenticationWindow
                {
                    DataContext = Locator.Current.GetService<AuthenticationViewModel>()
                };
            }
        }
        else
        {
            Log.Error("Application initialization failed. The application will now exit");
            ShutdownApplication(desktop, "Initialization failed.");
        }
    }

    private void ShutdownApplication(IClassicDesktopStyleApplicationLifetime desktop, string reason)
    {
        Log.Error("Shutting down application: {Reason}", reason);
        desktop.Shutdown();
    }
}