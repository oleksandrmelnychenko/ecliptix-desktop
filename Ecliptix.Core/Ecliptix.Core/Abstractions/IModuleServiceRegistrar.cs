using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Modularity;

public interface IModuleServiceRegistrar
{
    void RegisterServices(IServiceCollection services);
}
