using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Modularity;

public abstract class ModuleBase<TManifest> : ITypedModule<TManifest> where TManifest : IModuleManifest
{
    private bool _isLoaded;
    private IServiceProvider? _serviceProvider;
    protected ILogger? Logger;

    public abstract ModuleIdentifier Id { get; }
    public abstract TManifest Manifest { get; }
    IModuleManifest IModule.Manifest => Manifest;
    public bool IsLoaded => _isLoaded;
    public IModuleScope? ServiceScope { get; private set; }

    public virtual Task<bool> CanLoadAsync() => Task.FromResult(true);

    public async Task LoadAsync(IServiceProvider serviceProvider)
    {
        if (_isLoaded)
            return;

        try
        {
            _serviceProvider = serviceProvider;
            Logger = serviceProvider.GetService<ILogger<ModuleBase<TManifest>>>();
            Logger?.LogInformation("Loading module: {ModuleName}", Id.ToName());

            ModuleResourceManager? resourceManager = serviceProvider.GetService<ModuleResourceManager>();
            if (resourceManager != null)
            {
                ServiceScope = resourceManager.CreateModuleScope(Id.ToName());
                _serviceProvider = ServiceScope.ServiceProvider;
            }

            await OnLoadAsync();

            _isLoaded = true;

            Logger?.LogInformation("Module loaded successfully: {ModuleName}", Id.ToName());
        }
        catch (Exception ex)
        {
            ServiceScope?.Dispose();
            ServiceScope = null;
            Logger?.LogError(ex, "Failed to load module: {ModuleName}", Id.ToName());
            throw;
        }
    }

    public async Task UnloadAsync()
    {
        if (!_isLoaded)
            return;

        try
        {
            Logger?.LogInformation("Unloading module: {ModuleName}", Id.ToName());

            await OnUnloadAsync();

            ServiceScope?.Dispose();
            ServiceScope = null;

            _isLoaded = false;
            _serviceProvider = null;

            Logger?.LogInformation("Module unloaded successfully: {ModuleName}", Id.ToName());
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to unload module: {ModuleName}", Id.ToName());
            throw;
        }
    }

    public virtual Task SetupMessageHandlersAsync(IModuleMessageBus messageBus) => Task.CompletedTask;

    protected virtual Task OnLoadAsync() => Task.CompletedTask;

    protected virtual Task OnUnloadAsync() => Task.CompletedTask;

    protected T GetService<T>() where T : notnull
    {
        if (_serviceProvider == null)
            throw new InvalidOperationException($"Module {Id.ToName()} is not loaded");

        return _serviceProvider.GetRequiredService<T>();
    }
}
