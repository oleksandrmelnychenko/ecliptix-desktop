using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Data;
using Ecliptix.Core.Factories;
using Ecliptix.Core.Services;
using Ecliptix.Core.Settings;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.Views;
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
        var services = new ServiceCollection();
        services.AddSingleton<INavigationWindowService, NavigationWindowService>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<SignUpViewModel>();
        services.AddTransient<SignInViewModel>();       
        services.AddTransient<ForgotPasswordViewModel>();
        services.AddSingleton<AuthorizationViewModel>(provider =>
        {
            var navigationService = provider.GetRequiredService<INavigationWindowService>();
            var pageFactory = provider.GetRequiredService<PageFactory>();
            var initialView = provider.GetRequiredService<SignUpViewModel>(); // or whichever view you want as default
            return new AuthorizationViewModel(navigationService, pageFactory, initialView);
        });
        
        var pageFactories = new Dictionary<ApplicationPageNames, Func<PageViewModel>>();
        
        services.AddSingleton<PageFactory>(provider =>
        {
            pageFactories.Add(ApplicationPageNames.LOGIN, () => provider.GetRequiredService<SignInViewModel>());
            pageFactories.Add(ApplicationPageNames.REGISTRATION, () => provider.GetRequiredService<SignUpViewModel>());
            pageFactories.Add(ApplicationPageNames.FORGOT_PASSWORD, () => provider.GetRequiredService<ForgotPasswordViewModel>());
            pageFactories.Add(ApplicationPageNames.MAIN, () => provider.GetRequiredService<MainWindowViewModel>());
        
            return new PageFactory(pageFactories);
        });
        
        var serviceProvider = services.BuildServiceProvider();
        
        AppSettings? appSettings = Locator.Current.GetService<AppSettings>();
        if (appSettings == null)
        {
            //TODO: load store to get the settings.
        }

        bool IsAuthorized = false;
        
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var authViewModel = serviceProvider.GetRequiredService<AuthorizationViewModel>();
            if (IsAuthorized)
            {
                desktop.MainWindow = new MainWindow
                {
                    DataContext = serviceProvider.GetRequiredService<MainWindowViewModel>()
                };
            }
            else
            {
                desktop.MainWindow = new AuthorizationWindow
                {
                    DataContext = authViewModel 
                };
            }
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