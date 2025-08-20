using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModule
{
    string Name { get; }
    int Priority { get; }
    ModuleLoadingStrategy LoadingStrategy { get; }
    bool IsLoaded { get; }
    
    IReadOnlyList<string> DependsOn { get; }
    
    IModuleResourceConstraints ResourceConstraints { get; }
    
    IModuleScope? ServiceScope { get; }
    
    Task<bool> CanLoadAsync();
    Task LoadAsync(IServiceProvider serviceProvider);
    Task UnloadAsync();
    
    void RegisterServices(IServiceCollection services);
    
    void RegisterViews(IViewLocator viewLocator);
    
    IReadOnlyList<Type> GetViewTypes();
    
    IReadOnlyList<Type> GetViewModelTypes();
    
    Task SetupMessageHandlersAsync(IModuleMessageBus messageBus);
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