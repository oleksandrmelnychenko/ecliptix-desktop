using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Core.Modularity;

public abstract class ModuleBase<TManifest> : ITypedModule<TManifest> where TManifest : IModuleManifest
{
    public abstract ModuleIdentifier Id { get; }
    public abstract TManifest Manifest { get; }
    IModuleManifest IModule.Manifest => Manifest;
    public bool IsLoaded { get; private set; }
    public IModuleScope? ServiceScope { get; private set; }
    public virtual Task<bool> CanLoadAsync() => Task.FromResult(true);

    public async Task LoadAsync(IServiceProvider serviceProvider)
    {
        if (IsLoaded)
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

            IsLoaded = true;
        }
        catch
        {
            ServiceScope?.Dispose();
            ServiceScope = null;
        }
    }

    public async Task UnloadAsync()
    {
        if (!IsLoaded)
        {
            return;
        }

        await OnUnloadAsync();

        ServiceScope?.Dispose();
        ServiceScope = null;

        IsLoaded = false;
    }

    public virtual Task SetupMessageHandlersAsync(IModuleMessageBus messageBus) => Task.CompletedTask;

    protected virtual Task OnLoadAsync() => Task.CompletedTask;

    protected virtual Task OnUnloadAsync() => Task.CompletedTask;
}
