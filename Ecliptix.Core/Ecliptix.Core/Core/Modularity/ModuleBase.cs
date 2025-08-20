using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Modularity;

public abstract class ModuleBase : IModule
{
    private bool _isLoaded;
    private IServiceProvider? _serviceProvider;
    protected ILogger? Logger;

    public abstract string Name { get; }
    public virtual int Priority => 0;
    public virtual ModuleLoadingStrategy LoadingStrategy => ModuleLoadingStrategy.Lazy;
    public bool IsLoaded => _isLoaded;

    public virtual IReadOnlyList<string> DependsOn => Array.Empty<string>();
    
    public virtual IModuleResourceConstraints ResourceConstraints => ModuleResourceConstraints.Default;
    public IModuleScope? ServiceScope { get; private set; }

    public virtual async Task<bool> CanLoadAsync()
    {
        await Task.CompletedTask;
        return true;
    }

    public async Task LoadAsync(IServiceProvider serviceProvider)
    {
        if (_isLoaded)
            return;

        try
        {
            _serviceProvider = serviceProvider;
            Logger = serviceProvider.GetService<ILogger<ModuleBase>>();
            Logger?.LogInformation("Loading module: {ModuleName}", Name);
            
            ModuleResourceManager? resourceManager = serviceProvider.GetService<ModuleResourceManager>();
            if (resourceManager != null)
            {
                ServiceScope = resourceManager.CreateModuleScope(Name, ResourceConstraints, RegisterServices);
                _serviceProvider = ServiceScope.ServiceProvider;
            }
            
            await OnLoadAsync();
            
            _isLoaded = true;
            
            Logger?.LogInformation("Module loaded successfully: {ModuleName}", Name);
        }
        catch (Exception ex)
        {
            ServiceScope?.Dispose();
            ServiceScope = null;
            Logger?.LogError(ex, "Failed to load module: {ModuleName}", Name);
            throw;
        }
    }

    public async Task UnloadAsync()
    {
        if (!_isLoaded)
            return;

        try
        {
            Logger?.LogInformation("Unloading module: {ModuleName}", Name);
            
            await OnUnloadAsync();
            
            ServiceScope?.Dispose();
            ServiceScope = null;
            
            _isLoaded = false;
            _serviceProvider = null;
            
            Logger?.LogInformation("Module unloaded successfully: {ModuleName}", Name);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to unload module: {ModuleName}", Name);
            throw;
        }
    }

    public abstract void RegisterServices(IServiceCollection services);
    
    public virtual void RegisterViews(IViewLocator viewLocator)
    {
    }
    
    public virtual IReadOnlyList<Type> GetViewTypes()
    {
        return Array.Empty<Type>();
    }
    
    public virtual IReadOnlyList<Type> GetViewModelTypes()
    {
        return Array.Empty<Type>();
    }

    public virtual Task SetupMessageHandlersAsync(IModuleMessageBus messageBus)
    {
        return Task.CompletedTask;
    }

    protected virtual async Task OnLoadAsync()
    {
        await Task.CompletedTask;
    }

    protected virtual async Task OnUnloadAsync()
    {
        await Task.CompletedTask;
    }

    protected T GetService<T>() where T : notnull
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException($"Module {Name} is not loaded");
            
        return _serviceProvider.GetRequiredService<T>();
    }

}