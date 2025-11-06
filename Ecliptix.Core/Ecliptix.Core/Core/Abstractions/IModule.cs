using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModule
{
    ModuleIdentifier Id { get; }
    IModuleManifest Manifest { get; }

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
    EAGER,
    LAZY,
    BACKGROUND
}

public enum ModuleState
{
    NOT_LOADED,
    LOADING,
    LOADED,
    FAILED,
    UNLOADING,
    UNLOADED
}
