using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Modularity;
using Ecliptix.Core.Features.Main.ViewModels;

namespace Ecliptix.Core.Features.Main;

public record MainModuleManifest() : ModuleManifest(
    Id: ModuleIdentifier.Main,
    DisplayName: "Main Module",
    Version: new Version(1, 0, 0),
    Priority: 20,
    LoadingStrategy: ModuleLoadingStrategy.Lazy,
    Dependencies: new[] { ModuleIdentifier.Authentication },
    ResourceConstraints: ModuleResourceConstraints.Default,
    ViewFactories: new Dictionary<Type, Func<Control>>(),
    ServiceMappings: new Dictionary<Type, Type>()
);

public class MainModule : ModuleBase<MainModuleManifest>
{
    public override ModuleIdentifier Id => ModuleIdentifier.Main;
    public override MainModuleManifest Manifest { get; } = new();

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