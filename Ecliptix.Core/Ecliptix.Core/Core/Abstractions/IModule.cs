using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModule
{
    ModuleIdentifier Id { get; }
    IModuleManifest Manifest { get; }
    bool IsLoaded { get; }

    IModuleScope? ServiceScope { get; }

    Task<bool> CanLoadAsync();
    Task LoadAsync(IServiceProvider serviceProvider);
    Task UnloadAsync();

    Task SetupMessageHandlersAsync(IModuleMessageBus messageBus);
}

public interface ITypedModule<out TManifest> : IModule where TManifest : IModuleManifest
{
    new TManifest Manifest { get; }
}

public enum ModuleLoadingStrategy
{
    Eager,
    Lazy,
    Background
}

public enum ModuleState
{
    NotLoaded,
    Loading,
    Loaded,
    Failed,
    Unloading,
    Unloaded
}
