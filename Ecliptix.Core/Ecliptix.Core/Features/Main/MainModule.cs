using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Ecliptix.Core.Core.Abstractions;
using Ecliptix.Core.Core.Modularity;
using Ecliptix.Core.Features.Main.ViewModels;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Abstractions.Core;

namespace Ecliptix.Core.Features.Main;

public record MainModuleManifest() : ModuleManifest(
    Id: ModuleIdentifier.Main,
    DisplayName: "Main Module",
    Version: new Version(1, 0, 0),
    Priority: 20,
    LoadingStrategy: ModuleLoadingStrategy.Lazy,
    Dependencies: [],
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
    }

    public override IReadOnlyList<Type> GetViewTypes()
    {
        return [];
    }

    public override IReadOnlyList<Type> GetViewModelTypes() =>
    [
        typeof(MainViewModel)
    ];
}