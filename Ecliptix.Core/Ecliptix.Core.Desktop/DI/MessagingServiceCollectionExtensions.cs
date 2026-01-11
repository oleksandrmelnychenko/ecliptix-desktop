using Ecliptix.Core.Messaging.Core.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Abstractions.Membership;
using Ecliptix.Core.Shell.Services.Core;
using Ecliptix.Core.Shell.Services.Localization;
using Ecliptix.Core.Shell.Services.Membership;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Desktop.DI;

public static class MessagingServiceCollectionExtensions
{
    public static IServiceCollection AddMessagingInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IMessageBus, MessageBus>();
        services.AddSingleton<IConnectivityService, ConnectivityService>();
        services.AddSingleton<IGlobalModalService, GlobalModalService>(sp => new GlobalModalService(sp.GetRequiredService<IMessageBus>()));
        services.AddSingleton<ISideSheetService, SideSheetService>();
        services.AddSingleton<IBottomSheetService, BottomSheetService>();
        services.AddSingleton<IOverlaySheetService, OverlaySheetService>();
        services.AddSingleton<IProfileMenuService, ProfileMenuService>();
        services.AddSingleton<ILanguageDetectionService, LanguageDetectionService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddTransient<ILogoutService, LogoutService>();

        services.AddSingleton<IApplicationStateManager, ApplicationStateManager>();
        services.AddSingleton<IApplicationRouter, ApplicationRouter>();
        services.AddTransient<ApplicationStartup>();

        return services;
    }
}
