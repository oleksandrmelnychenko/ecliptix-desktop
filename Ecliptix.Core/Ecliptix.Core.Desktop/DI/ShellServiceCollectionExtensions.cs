using Ecliptix.Core.Shell.Configuration;
using Ecliptix.Core.Shell.Factories;
using Ecliptix.Core.Shell.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Desktop.DI;

public static class ShellServiceCollectionExtensions
{
    public static IServiceCollection AddShellServices(this IServiceCollection services)
    {
        services.AddSingleton<MainWindowConfiguration>();
        services.AddSingleton<IWindowAnimationService, WindowAnimationService>();
        services.AddSingleton<IWindowPositionService, WindowPositionService>();
        services.AddSingleton<IViewModelFactory, ViewModelFactory>();

        return services;
    }
}
