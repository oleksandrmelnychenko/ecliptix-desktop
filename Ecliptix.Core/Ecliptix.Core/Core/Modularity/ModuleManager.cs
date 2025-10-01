using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Modularity;

public class ModuleManager : IModuleManager
{
    private readonly IModuleCatalog _catalog;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ModuleManager> _logger;
    private readonly ParallelModuleLoader _parallelLoader;
    private readonly IModuleMessageBus _messageBus;
    private readonly ConcurrentDictionary<string, ModuleState> _moduleStates = new();

    public event EventHandler<ModuleLoadedEventArgs>? ModuleLoaded;
    public event EventHandler<ModuleUnloadedEventArgs>? ModuleUnloaded;
    public event EventHandler<ModuleLoadingEventArgs>? ModuleLoading;
    public event EventHandler<ModuleFailedEventArgs>? ModuleFailed;

    public ModuleManager(IModuleCatalog catalog, IServiceProvider serviceProvider, ILogger<ModuleManager> logger)
    {
        _catalog = catalog;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _parallelLoader = new ParallelModuleLoader(serviceProvider);
        _messageBus = serviceProvider.GetService<IModuleMessageBus>() ??
                      throw new InvalidOperationException("IModuleMessageBus not registered");

        foreach (IModule module in _catalog.GetModules())
        {
            _moduleStates[module.Id.ToName()] = ModuleState.NotLoaded;
        }
    }

    public Task<IEnumerable<IModuleMetadata>> DiscoverModulesAsync()
    {
        IEnumerable<IModuleMetadata> modules = _catalog.GetModules().Select(m => new ModuleMetadata
        {
            Name = m.Id.ToName(),
            AssemblyPath = string.Empty,
            LoadingStrategy = m.Manifest.LoadingStrategy,
            Priority = m.Manifest.Priority,
            Dependencies = m.Manifest.Dependencies.Select(d => d.ToName()).ToArray()
        });
        return Task.FromResult(modules);
    }

    public async Task<IModule> LoadModuleAsync(ModuleIdentifier moduleId)
    {
        return await LoadModuleAsync(moduleId.ToName());
    }

    public async Task<IModule> LoadModuleAsync(string moduleName)
    {
        IModule? module = _catalog.GetModule(moduleName);
        if (module == null)
            throw new InvalidOperationException($"Module '{moduleName}' not found in catalog");

        if (IsModuleLoaded(moduleName))
            return module;

        _moduleStates[moduleName] = ModuleState.Loading;
        ModuleLoading?.Invoke(this, new ModuleLoadingEventArgs { ModuleName = moduleName });

        try
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            if (!await module.CanLoadAsync())
            {
                _moduleStates[moduleName] = ModuleState.Failed;
                throw new InvalidOperationException($"Module '{moduleName}' cannot be loaded at this time");
            }

            await LoadDependenciesAsync(module);

            await module.LoadAsync(_serviceProvider);


            stopwatch.Stop();
            _moduleStates[moduleName] = ModuleState.Loaded;

            try
            {
                await module.SetupMessageHandlersAsync(_messageBus);
                _logger.LogDebug("Message handlers setup completed for module: {ModuleName}", module.Id.ToName());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to setup message handlers for module: {ModuleName}", module.Id.ToName());
            }

            ModuleLoaded?.Invoke(this, new ModuleLoadedEventArgs
            {
                ModuleName = moduleName,
                LoadTime = stopwatch.Elapsed
            });

            _logger.LogInformation("Module '{ModuleName}' loaded in {LoadTime}ms", module.Id.ToName(),
                stopwatch.ElapsedMilliseconds);

            return module;
        }
        catch (Exception ex)
        {
            _moduleStates[moduleName] = ModuleState.Failed;
            ModuleFailed?.Invoke(this, new ModuleFailedEventArgs { ModuleName = moduleName, Exception = ex });
            throw;
        }
    }

    public async Task LoadModulesAsync(ModuleLoadingStrategy strategy)
    {
        IModule[] modulesToLoad = _catalog.GetModules()
            .Where(m => m.Manifest.LoadingStrategy == strategy && !IsModuleLoaded(m.Id.ToName()))
            .ToArray();

        if (modulesToLoad.Length == 0)
        {
            _logger.LogDebug("No modules to load for strategy: {Strategy}", strategy);
            return;
        }

        _logger.LogInformation("Loading {Count} modules with strategy: {Strategy}", modulesToLoad.Length, strategy);

        try
        {
            IReadOnlyList<IModule> loadedModules = await _parallelLoader.LoadModulesAsync(modulesToLoad);

            foreach (IModule module in loadedModules)
            {
                _moduleStates[module.Id.ToName()] = ModuleState.Loaded;

                try
                {
                    await module.SetupMessageHandlersAsync(_messageBus);
                    _logger.LogDebug("Message handlers setup completed for module: {ModuleName}", module.Id.ToName());
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to setup message handlers for module: {ModuleName}",
                        module.Id.ToName());
                }

                ModuleLoaded?.Invoke(this, new ModuleLoadedEventArgs
                {
                    ModuleName = module.Id.ToName(),
                    LoadTime = TimeSpan.Zero
                });
            }

            _logger.LogInformation("Successfully loaded {Count} modules with strategy: {Strategy}", loadedModules.Count,
                strategy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load modules with strategy: {Strategy}", strategy);

            foreach (IModule module in modulesToLoad)
            {
                if (_moduleStates.TryGetValue(module.Id.ToName(), out ModuleState state) &&
                    state == ModuleState.Loading)
                {
                    _moduleStates[module.Id.ToName()] = ModuleState.Failed;
                    ModuleFailed?.Invoke(this, new ModuleFailedEventArgs
                    {
                        ModuleName = module.Id.ToName(),
                        Exception = ex
                    });
                }
            }

            throw;
        }
    }

    public async Task LoadEagerModulesAsync()
    {
        _logger.LogInformation("Loading eager modules...");
        await LoadModulesAsync(ModuleLoadingStrategy.Eager);
        _logger.LogInformation("Eager modules loaded successfully");
    }

    public async Task<IReadOnlyList<IModule>> LoadAllModulesAsync()
    {
        IModule[] unloadedModules = _catalog.GetModules()
            .Where(m => !IsModuleLoaded(m.Id.ToName()))
            .ToArray();

        if (unloadedModules.Length == 0)
        {
            _logger.LogDebug("All modules are already loaded");
            return [];
        }

        _logger.LogInformation("Loading all {Count} modules in dependency order", unloadedModules.Length);

        IEnumerable<IModule> orderedModules = await _catalog.GetLoadOrderAsync();
        IModule[] modulesToLoad = orderedModules
            .Where(m => !IsModuleLoaded(m.Id.ToName()))
            .ToArray();

        return await _parallelLoader.LoadModulesAsync(modulesToLoad);
    }

    public void StartBackgroundPreloading()
    {
        IModule[] backgroundModules = _catalog.GetModules()
            .Where(m => m.Manifest.LoadingStrategy == ModuleLoadingStrategy.Background &&
                        !IsModuleLoaded(m.Id.ToName()))
            .ToArray();

        if (backgroundModules.Length > 0)
        {
            _logger.LogInformation("Starting background preloading for {Count} modules", backgroundModules.Length);
            _parallelLoader.PreloadModulesAsync(backgroundModules);
        }
    }

    public ModuleLoadingStats GetLoadingStats()
    {
        return _parallelLoader.GetLoadingStats();
    }

    public async Task UnloadModuleAsync(string moduleName)
    {
        IModule? module = _catalog.GetModule(moduleName);
        if (module == null || !IsModuleLoaded(moduleName))
            return;

        _moduleStates[moduleName] = ModuleState.Unloading;

        try
        {
            await module.UnloadAsync();
            _moduleStates[moduleName] = ModuleState.Unloaded;

            ModuleResourceManager? resourceManager = _serviceProvider.GetService<ModuleResourceManager>();
            resourceManager?.RemoveModuleScope(moduleName);

            ModuleUnloaded?.Invoke(this, new ModuleUnloadedEventArgs { ModuleName = moduleName });

            _logger.LogInformation("Module '{ModuleName}' unloaded", moduleName);
        }
        catch (Exception ex)
        {
            _moduleStates[moduleName] = ModuleState.Failed;
            _logger.LogError(ex, "Failed to unload module '{ModuleName}'", moduleName);
            throw;
        }
    }

    public Task UnloadInactiveModulesAsync(TimeSpan inactiveThreshold)
    {
        _logger.LogDebug("UnloadInactiveModulesAsync is deprecated and does nothing");
        return Task.CompletedTask;
    }

    public bool IsModuleLoaded(string moduleName) =>
        _moduleStates.TryGetValue(moduleName, out ModuleState state) && state == ModuleState.Loaded;

    public IReadOnlyDictionary<string, ModuleState> GetModuleStates() =>
        new Dictionary<string, ModuleState>(_moduleStates);

    private async Task LoadDependenciesAsync(IModule module)
    {
        if (module.Manifest.Dependencies.Count == 0)
        {
            return;
        }

        try
        {
            IEnumerable<IModule> requiredModules = await _catalog.GetRequiredModulesAsync(module.Id);

            foreach (IModule dependency in requiredModules)
            {
                if (!IsModuleLoaded(dependency.Id.ToName()))
                {
                    await LoadModuleAsync(dependency.Id.ToName());
                }
            }
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("Falling back to manual dependency resolution for module: {ModuleName}",
                module.Id.ToName());
            foreach (ModuleIdentifier dependency in module.Manifest.Dependencies)
            {
                if (!IsModuleLoaded(dependency.ToName()))
                {
                    await LoadModuleAsync(dependency);
                }
            }
        }
    }


    private class ModuleMetadata : IModuleMetadata
    {
        public string Name { get; init; } = string.Empty;
        public string AssemblyPath { get; init; } = string.Empty;
        public ModuleLoadingStrategy LoadingStrategy { get; init; }
        public int Priority { get; init; }
        public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();
    }
}