using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Core.Services.Core;
using Ecliptix.Network.Security.KeySplitting;
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
