using Ecliptix.Core.Views.Core.Configuration;
using Ecliptix.Core.Views.Core.Factories;
using Ecliptix.Core.Views.Core.Services;
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
