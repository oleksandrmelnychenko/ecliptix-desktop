using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Modularity;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModuleManager
{
    Task<IEnumerable<IModuleMetadata>> DiscoverModulesAsync();

    Task<IModule> LoadModuleAsync(string moduleName);
    Task LoadModulesAsync(ModuleLoadingStrategy strategy);
    Task LoadEagerModulesAsync();
    Task<IReadOnlyList<IModule>> LoadAllModulesAsync();
    void StartBackgroundPreloading();
    ModuleLoadingStats GetLoadingStats();

    Task UnloadModuleAsync(string moduleName);
    Task UnloadInactiveModulesAsync(TimeSpan inactiveThreshold);

    bool IsModuleLoaded(string moduleName);
    IReadOnlyDictionary<string, ModuleState> GetModuleStates();

    event EventHandler<ModuleLoadedEventArgs> ModuleLoaded;
    event EventHandler<ModuleUnloadedEventArgs> ModuleUnloaded;
    event EventHandler<ModuleLoadingEventArgs> ModuleLoading;
    event EventHandler<ModuleFailedEventArgs> ModuleFailed;
}

public interface IModuleMetadata
{
    string Name { get; }
    string AssemblyPath { get; }
    ModuleLoadingStrategy LoadingStrategy { get; }
    int Priority { get; }
    IReadOnlyList<string> Dependencies { get; }
}

public class ModuleLoadedEventArgs : EventArgs
{
    public required string ModuleName { get; init; }
    public TimeSpan LoadTime { get; init; }
}

public class ModuleUnloadedEventArgs : EventArgs
{
    public required string ModuleName { get; init; }
}

public class ModuleLoadingEventArgs : EventArgs
{
    public required string ModuleName { get; init; }
}

public class ModuleFailedEventArgs : EventArgs
{
    public required string ModuleName { get; init; }
    public Exception? Exception { get; init; }
}