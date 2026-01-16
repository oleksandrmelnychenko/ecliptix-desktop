using System.Threading.Tasks;
using Ecliptix.Core.Modularity.Messaging;
using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Modularity;
using Ecliptix.Core.Modularity.Authentication;
using Ecliptix.Core.Settings;
using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.ViewModels;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.Services.Abstractions.Authentication;
using Ecliptix.Feature.Authentication.Services.Authentication;
using Ecliptix.Feature.Authentication.ViewModels.Hosts;
using Ecliptix.Network.Infrastructure.Data.Abstractions;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Microsoft.Extensions.DependencyInjection;
using IMessageBus = Ecliptix.Core.Messaging.Core.Messaging.IMessageBus;

namespace Ecliptix.Feature.Authentication;

public record AuthenticationModuleManifest() : ModuleManifest(
    Priority: 30,
    LoadingStrategy: ModuleLoadingStrategy.EAGER,
    Dependencies: []
);

public class AuthenticationModule : ModuleBase<AuthenticationModuleManifest>, IModuleServiceRegistrar
{
    public override ModuleIdentifier Id => ModuleIdentifier.AUTHENTICATION;
    public override AuthenticationModuleManifest Manifest { get; } = new();

    public void RegisterServices(IServiceCollection services)
    {
        FeatureRegistration.RegisterServices(services);

        services.AddSingleton<IAuthenticationService, OpaqueAuthenticationService>();
        services.AddSingleton<IOpaqueRegistrationService, OpaqueRegistrationService>();
        services.AddSingleton<ISecureKeyRecoveryService, SecureKeyRecoveryService>();

        services.AddTransient<AuthenticationViewModel>(sp => new AuthenticationViewModel(
            new AuthenticationViewModelDependencies
            {
                ConnectivityService = sp.GetRequiredService<IConnectivityService>(),
                NetworkProvider = sp.GetRequiredService<NetworkProvider>(),
                LocalizationService = sp.GetRequiredService<ILocalizationService>(),
                StorageProvider = sp.GetRequiredService<IApplicationSecureStorageProvider>(),
                LanguageDetectionService = sp.GetRequiredService<ILanguageDetectionService>(),
                Router = sp.GetRequiredService<IApplicationRouter>(),
                GlobalModalService = sp.GetRequiredService<IGlobalModalService>(),
                MainWindowViewModel = sp.GetRequiredService<MainWindowViewModel>(),
                Settings = sp.GetRequiredService<DefaultSystemSettings>(),
                MessageBus = sp.GetRequiredService<IMessageBus>(),
                AuthRepository = sp.GetRequiredService<IAuthRepository>(),
            }));
        services.AddTransient<IAuthenticationHost>(sp => sp.GetRequiredService<AuthenticationViewModel>());
    }

    public override async Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        await messageBus.PublishAsync(new ModuleInitializedEvent
        {
            ModuleName = Id.ToName()
        });
    }
}
