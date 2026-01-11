using Ecliptix.Core.Shell.Abstractions.Core;
using Ecliptix.Core.Shell.Services.Core;
using Ecliptix.Feature.Authentication.Services.Abstractions.Authentication;
using Ecliptix.Feature.Authentication.Services.Authentication;
using Ecliptix.Network.Infrastructure.Security.KeySplitting;
using Ecliptix.Network.Services.Abstractions.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Desktop.DI;

public static class AuthServiceCollectionExtensions
{
    public static IServiceCollection AddAuthInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAuthenticationService, OpaqueAuthenticationService>();
        services.AddSingleton<IOpaqueRegistrationService, OpaqueRegistrationService>();
        services.AddSingleton<ISecureKeyRecoveryService, SecureKeyRecoveryService>();
        services.AddSingleton<IIdentityService, IdentityService>();

        services.AddSingleton<IHardenedKeyDerivation, HardenedKeyDerivation>();

        services.AddSingleton<IApplicationInitializer, ApplicationInitializer>();

        return services;
    }
}
