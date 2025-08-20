using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

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

    void RegisterServices(IServiceCollection services);

    void RegisterViews(IViewLocator viewLocator);

    void RegisterViewFactories(IModuleViewFactory viewFactory);

    IReadOnlyList<Type> GetViewTypes();

    IReadOnlyList<Type> GetViewModelTypes();

    Task SetupMessageHandlersAsync(IModuleMessageBus messageBus);
}

public interface ITypedModule<TManifest> : IModule where TManifest : IModuleManifest
{
    new TManifest Manifest { get; }
}

public enum ModuleLoadingStrategy
{
    Eager,
    Lazy,
    OnDemand,
    Background,
    Conditional
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