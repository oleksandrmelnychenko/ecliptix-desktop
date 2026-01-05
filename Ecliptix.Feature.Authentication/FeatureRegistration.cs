using Ecliptix.Core.Modularity.Modularity;
using Ecliptix.Feature.Authentication.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.Authentication.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Feature.Authentication;

public static class FeatureRegistration
{
    public static void RegisterModule(ModuleCatalog catalog) => catalog.AddModule<AuthenticationModule>();

    public static void RegisterServices(IServiceCollection services)
    {
        services.AddSingleton<IAuthRepository, AuthRepository>();
    }
}
