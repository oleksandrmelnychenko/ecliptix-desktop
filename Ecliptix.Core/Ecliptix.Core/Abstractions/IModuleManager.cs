using Ecliptix.Utilities;

namespace Ecliptix.Core.Modularity;

public interface IModuleManager
{
    Task<Option<IModule>> LoadModuleAsync(string moduleName);
    Task LoadEagerModulesAsync();
    Task UnloadModuleAsync(string moduleName);
}
