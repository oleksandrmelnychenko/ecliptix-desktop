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
    private readonly ConcurrentDictionary<string, DateTime> _lastAccessTimes = new();

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
        _messageBus = serviceProvider.GetService<IModuleMessageBus>() ?? throw new InvalidOperationException("IModuleMessageBus not registered");
        
        foreach (IModule module in _catalog.GetModules())
        {
            _moduleStates[module.Name] = ModuleState.NotLoaded;
        }
    }

    public async Task<IEnumerable<IModuleMetadata>> DiscoverModulesAsync()
    {
        await Task.CompletedTask;
        
        return _catalog.GetModules().Select(m => new ModuleMetadata
        {
            Name = m.Name,
            AssemblyPath = string.Empty,
            LoadingStrategy = m.LoadingStrategy,
            Priority = m.Priority,
            Dependencies = m.DependsOn
        });
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
            
            // Register view mappings with the ViewLocator
            IViewLocator? viewLocator = _serviceProvider.GetService<IViewLocator>();
            if (viewLocator != null)
            {
                module.RegisterViews(viewLocator);
                _logger.LogDebug("View mappings registered for module: {ModuleName}", moduleName);
            }
            else
            {
                _logger.LogWarning("IViewLocator not found in service provider - views not registered for module: {ModuleName}", moduleName);
            }
            
            stopwatch.Stop();
            _moduleStates[moduleName] = ModuleState.Loaded;
            _lastAccessTimes[moduleName] = DateTime.UtcNow;

            try
            {
                await module.SetupMessageHandlersAsync(_messageBus);
                _logger.LogDebug("Message handlers setup completed for module: {ModuleName}", moduleName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to setup message handlers for module: {ModuleName}", moduleName);
            }

            ModuleLoaded?.Invoke(this, new ModuleLoadedEventArgs 
            { 
                ModuleName = moduleName, 
                LoadTime = stopwatch.Elapsed 
            });

            _logger.LogInformation("Module '{ModuleName}' loaded in {LoadTime}ms", moduleName, stopwatch.ElapsedMilliseconds);
            
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
            .Where(m => m.LoadingStrategy == strategy && !IsModuleLoaded(m.Name))
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
                _moduleStates[module.Name] = ModuleState.Loaded;
                _lastAccessTimes[module.Name] = DateTime.UtcNow;
                
                IViewLocator? viewLocator = _serviceProvider.GetService<IViewLocator>();
                if (viewLocator != null)
                {
                    module.RegisterViews(viewLocator);
                    _logger.LogDebug("View mappings registered for module: {ModuleName}", module.Name);
                }
                else
                {
                    _logger.LogWarning("IViewLocator not found in service provider - views not registered for module: {ModuleName}", module.Name);
                }
                
                try
                {
                    await module.SetupMessageHandlersAsync(_messageBus);
                    _logger.LogDebug("Message handlers setup completed for module: {ModuleName}", module.Name);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to setup message handlers for module: {ModuleName}", module.Name);
                }
                
                ModuleLoaded?.Invoke(this, new ModuleLoadedEventArgs 
                { 
                    ModuleName = module.Name, 
                    LoadTime = TimeSpan.Zero
                });
            }

            _logger.LogInformation("Successfully loaded {Count} modules with strategy: {Strategy}", loadedModules.Count, strategy);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load modules with strategy: {Strategy}", strategy);
            
            foreach (IModule module in modulesToLoad)
            {
                if (_moduleStates.TryGetValue(module.Name, out ModuleState state) && state == ModuleState.Loading)
                {
                    _moduleStates[module.Name] = ModuleState.Failed;
                    ModuleFailed?.Invoke(this, new ModuleFailedEventArgs 
                    { 
                        ModuleName = module.Name, 
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
            .Where(m => !IsModuleLoaded(m.Name))
            .ToArray();

        if (unloadedModules.Length == 0)
        {
            _logger.LogDebug("All modules are already loaded");
            return [];
        }

        _logger.LogInformation("Loading all {Count} modules in dependency order", unloadedModules.Length);
        
        IEnumerable<IModule> orderedModules = await _catalog.GetLoadOrderAsync();
        IModule[] modulesToLoad = orderedModules
            .Where(m => !IsModuleLoaded(m.Name))
            .ToArray();
            
        return await _parallelLoader.LoadModulesAsync(modulesToLoad);
    }

    public void StartBackgroundPreloading()
    {
        IModule[] backgroundModules = _catalog.GetModules()
            .Where(m => m.LoadingStrategy == ModuleLoadingStrategy.Background && !IsModuleLoaded(m.Name))
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
            _lastAccessTimes.TryRemove(moduleName, out _);

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

    public async Task UnloadInactiveModulesAsync(TimeSpan inactiveThreshold)
    {
        DateTime cutoffTime = DateTime.UtcNow - inactiveThreshold;
        
        IEnumerable<string> inactiveModules = _lastAccessTimes
            .Where(kvp => kvp.Value < cutoffTime && IsModuleLoaded(kvp.Key))
            .Select(kvp => kvp.Key);

        foreach (string moduleName in inactiveModules)
        {
            try
            {
                await UnloadModuleAsync(moduleName);
                _logger.LogInformation("Unloaded inactive module '{ModuleName}'", moduleName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unload inactive module '{ModuleName}'", moduleName);
            }
        }
    }

    public bool IsModuleLoaded(string moduleName) => 
        _moduleStates.TryGetValue(moduleName, out ModuleState state) && state == ModuleState.Loaded;

    public IReadOnlyDictionary<string, ModuleState> GetModuleStates() => 
        new Dictionary<string, ModuleState>(_moduleStates);

    private async Task LoadDependenciesAsync(IModule module)
    {
        if (module.DependsOn.Count == 0)
        {
            return;
        }

        try
        {
            IEnumerable<IModule> requiredModules = await _catalog.GetRequiredModulesAsync(module.Name);
            
            foreach (IModule dependency in requiredModules)
            {
                if (!IsModuleLoaded(dependency.Name))
                {
                    await LoadModuleAsync(dependency.Name);
                }
            }
        }
        catch (InvalidOperationException)
        {
            _logger.LogWarning("Falling back to manual dependency resolution for module: {ModuleName}", module.Name);
            foreach (string dependency in module.DependsOn)
            {
                if (!IsModuleLoaded(dependency))
                {
                    await LoadModuleAsync(dependency);
                }
            }
        }
    }

    public void UpdateLastAccessTime(string moduleName)
    {
        if (IsModuleLoaded(moduleName))
        {
            _lastAccessTimes[moduleName] = DateTime.UtcNow;
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