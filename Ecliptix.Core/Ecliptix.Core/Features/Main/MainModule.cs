using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Modularity;
using Ecliptix.Core.Features.Main.ViewModels;

namespace Ecliptix.Core.Features.Main;

public class MainModule : ModuleBase
{
    public override string Name => "Main";
    public override int Priority => 20; 
    public override ModuleLoadingStrategy LoadingStrategy => ModuleLoadingStrategy.Lazy; 

    public override IReadOnlyList<string> DependsOn => new[] { "Authentication" }; 

    public override void RegisterServices(IServiceCollection services)
    {
        
        services.AddTransient<MainViewModel>();
    }

    public override void RegisterViews(IViewLocator viewLocator)
    {
        // No views to register yet - MainViewModel doesn't have a corresponding view
    }
    
    public override IReadOnlyList<Type> GetViewTypes()
    {
        // No views yet
        return Array.Empty<Type>();
    }
    
    public override IReadOnlyList<Type> GetViewModelTypes()
    {
        return new[]
        {
            typeof(MainViewModel)
        };
    }
}