using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModuleManager
{
    Task<Option<IModule>> LoadModuleAsync(string moduleName);
    Task LoadEagerModulesAsync();
    Task UnloadModuleAsync(string moduleName);
}
