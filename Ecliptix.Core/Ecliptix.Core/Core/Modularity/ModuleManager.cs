using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Utilities;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;

public class ModuleManager : IModuleManager
{
    private readonly IModuleCatalog _catalog;
    private readonly IServiceProvider _serviceProvider;
    private readonly IModuleMessageBus _messageBus;
    private readonly ConcurrentDictionary<string, ModuleState> _moduleStates = new();

    public ModuleManager(IModuleCatalog catalog, IServiceProvider serviceProvider)
    {
        _catalog = catalog;
        _serviceProvider = serviceProvider;
        _messageBus = serviceProvider.GetService<IModuleMessageBus>()!;

        foreach (IModule module in _catalog.GetModules())
        {
            _moduleStates[module.Id.ToName()] = ModuleState.NOT_LOADED;
        }
    }

    public async Task<Option<IModule>> LoadModuleAsync(string moduleName)
    {
        Option<IModule> moduleOption = _catalog.GetModule(moduleName);
        if (!moduleOption.IsSome)
        {
            Log.Warning("Module '{ModuleName}' not found in catalog", moduleName);
            return Option<IModule>.None;
        }

        IModule module = moduleOption.Value!;

        if (!_moduleStates.TryGetValue(moduleName, out ModuleState currentState))
        {
            Log.Error("Module '{ModuleName}' state not initialized", moduleName);
            return Option<IModule>.None;
        }

        if (currentState == ModuleState.LOADED)
        {
            return Option<IModule>.Some(module);
        }

        if (!_moduleStates.TryUpdate(moduleName, ModuleState.LOADING, currentState))
        {
            if (_moduleStates.TryGetValue(moduleName, out ModuleState updatedState) &&
                updatedState == ModuleState.LOADED)
            {
                return Option<IModule>.Some(module);
            }

            Log.Warning("Module '{ModuleName}' is already being loaded or in an invalid state", moduleName);
            return Option<IModule>.None;
        }

        IReadOnlyList<ModuleIdentifier> dependencies = module.Manifest.Dependencies;
        if (dependencies.Count > 0)
        {
            Log.Information("Module '{ModuleName}' has {Count} dependencies, loading them first",
                moduleName, dependencies.Count);

            foreach (ModuleIdentifier dependencyId in dependencies)
            {
                string dependencyName = dependencyId.ToName();

                if (!IsModuleLoaded(dependencyName))
                {
                    Log.Information("Loading dependency '{DependencyName}' for module '{ModuleName}'",
                        dependencyName, moduleName);

                    Option<IModule> dependencyResult = await LoadModuleAsync(dependencyName);

                    if (!dependencyResult.IsSome)
                    {
                        _moduleStates[moduleName] = ModuleState.FAILED;
                        Log.Error("Failed to load dependency '{DependencyName}' for module '{ModuleName}'",
                            dependencyName, moduleName);
                        return Option<IModule>.None;
                    }
                }
            }
        }

        if (!await module.CanLoadAsync())
        {
            _moduleStates[moduleName] = ModuleState.FAILED;
            Log.Warning("Module '{ModuleName}' cannot be loaded at this time", moduleName);
            return Option<IModule>.None;
        }

        await module.LoadAsync(_serviceProvider);

        _moduleStates[moduleName] = ModuleState.LOADED;

        await module.SetupMessageHandlersAsync(_messageBus);
        Log.Information("Module '{ModuleName}' loaded successfully", moduleName);
        return Option<IModule>.Some(module);
    }

    public async Task LoadModulesAsync(ModuleLoadingStrategy strategy)
    {
        IModule[] modulesToLoad = _catalog.GetModules()
            .Where(m => m.Manifest.LoadingStrategy == strategy && !IsModuleLoaded(m.Id.ToName()))
            .ToArray();

        if (modulesToLoad.Length == 0)
        {
            return;
        }

        foreach (IModule module in modulesToLoad)
        {
            await LoadModuleAsync(module.Id.ToName());
        }
    }

    public async Task LoadEagerModulesAsync()
    {
        await LoadModulesAsync(ModuleLoadingStrategy.EAGER);
    }

    public async Task<IReadOnlyList<IModule>> LoadAllModulesAsync()
    {
        IModule[] unloadedModules = _catalog.GetModules()
            .Where(m => !IsModuleLoaded(m.Id.ToName()))
            .ToArray();

        if (unloadedModules.Length == 0)
        {
            return [];
        }

        List<IModule> loadedModules = new();
        foreach (IModule module in unloadedModules)
        {
            Option<IModule> result = await LoadModuleAsync(module.Id.ToName());
            if (result.IsSome)
            {
                loadedModules.Add(result.Value!);
            }
        }

        return loadedModules;
    }

    public void StartBackgroundPreloading()
    {
        IModule[] backgroundModules = _catalog.GetModules()
            .Where(m => m.Manifest.LoadingStrategy == ModuleLoadingStrategy.BACKGROUND &&
                        !IsModuleLoaded(m.Id.ToName()))
            .ToArray();

        if (backgroundModules.Length > 0)
        {
            Task.Run(async () =>
            {
                foreach (IModule module in backgroundModules)
                {
                    await LoadModuleAsync(module.Id.ToName());
                }
            });
        }
    }

    public async Task<IReadOnlyList<IModule>> LoadModulesInParallelAsync(params string[] moduleNames)
    {
        Task<Option<IModule>>[] loadTasks = moduleNames
            .Select(moduleName => LoadModuleAsync(moduleName))
            .ToArray();

        Option<IModule>[] results = await Task.WhenAll(loadTasks);

        List<IModule> loadedModules = new();
        foreach (Option<IModule> result in results)
        {
            if (result.IsSome)
            {
                loadedModules.Add(result.Value!);
            }
        }

        Log.Information("Loaded {Count} modules in parallel: {Modules}",
            loadedModules.Count,
            string.Join(", ", loadedModules.Select(m => m.Id.ToName())));

        return loadedModules;
    }

    public async Task<IReadOnlyList<IModule>> LoadModulesByStrategyInParallelAsync(ModuleLoadingStrategy strategy)
    {
        string[] moduleNames = _catalog.GetModules()
            .Where(m => m.Manifest.LoadingStrategy == strategy && !IsModuleLoaded(m.Id.ToName()))
            .Select(m => m.Id.ToName())
            .ToArray();

        if (moduleNames.Length == 0)
        {
            return [];
        }

        return await LoadModulesInParallelAsync(moduleNames);
    }

    public async Task UnloadModuleAsync(string moduleName)
    {
        Option<IModule> moduleOption = _catalog.GetModule(moduleName);
        if (!moduleOption.IsSome || !IsModuleLoaded(moduleName))
        {
            return;
        }

        IModule module = moduleOption.Value!;

        _moduleStates[moduleName] = ModuleState.UNLOADING;

        await module.UnloadAsync();
        _moduleStates[moduleName] = ModuleState.UNLOADED;

        ModuleResourceManager? resourceManager = _serviceProvider.GetService<ModuleResourceManager>();
        resourceManager?.RemoveModuleScope(moduleName);
    }

    public bool IsModuleLoaded(string moduleName) =>
        _moduleStates.TryGetValue(moduleName, out ModuleState state) && state == ModuleState.LOADED;

    public IReadOnlyDictionary<string, ModuleState> GetModuleStates() =>
        new Dictionary<string, ModuleState>(_moduleStates);
}
