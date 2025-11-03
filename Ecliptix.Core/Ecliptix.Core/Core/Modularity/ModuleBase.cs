using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Core.Modularity;

public abstract class ModuleBase<TManifest> : ITypedModule<TManifest> where TManifest : IModuleManifest
{
    private bool _isLoaded;

    public abstract ModuleIdentifier Id { get; }
    public abstract TManifest Manifest { get; }
    IModuleManifest IModule.Manifest => Manifest;
    public bool IsLoaded => _isLoaded;
    public IModuleScope? ServiceScope { get; private set; }

    public virtual Task<bool> CanLoadAsync() => Task.FromResult(true);

    public async Task LoadAsync(IServiceProvider serviceProvider)
    {
        if (_isLoaded)
        {
            return;
        }

        try
        {
            ModuleResourceManager? resourceManager = serviceProvider.GetService<ModuleResourceManager>();
            if (resourceManager != null)
            {
                ServiceScope = resourceManager.CreateModuleScope(Id.ToName());
            }

            await OnLoadAsync();

            _isLoaded = true;
        }
        catch
        {
            ServiceScope?.Dispose();
            ServiceScope = null;
        }
    }

    public async Task UnloadAsync()
    {
        if (!_isLoaded)
        {
            return;
        }

        await OnUnloadAsync();

        ServiceScope?.Dispose();
        ServiceScope = null;

        _isLoaded = false;
    }

    public virtual Task SetupMessageHandlersAsync(IModuleMessageBus messageBus) => Task.CompletedTask;

    protected virtual Task OnLoadAsync() => Task.CompletedTask;

    protected virtual Task OnUnloadAsync() => Task.CompletedTask;
}
