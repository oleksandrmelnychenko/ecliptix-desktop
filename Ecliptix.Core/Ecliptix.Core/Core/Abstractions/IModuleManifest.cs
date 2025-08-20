using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModuleManifest
{
    ModuleIdentifier Id { get; }
    string DisplayName { get; }
    Version Version { get; }
    int Priority { get; }
    ModuleLoadingStrategy LoadingStrategy { get; }

    IReadOnlyList<ModuleIdentifier> Dependencies { get; }

    IModuleResourceConstraints ResourceConstraints { get; }

    IReadOnlyDictionary<Type, Func<Control>> ViewFactories { get; }

    IReadOnlyDictionary<Type, Type> ServiceMappings { get; }

    bool CanLoad();
}

public record ModuleManifest(
    ModuleIdentifier Id,
    string DisplayName,
    Version Version,
    int Priority,
    ModuleLoadingStrategy LoadingStrategy,
    IReadOnlyList<ModuleIdentifier> Dependencies,
    IModuleResourceConstraints ResourceConstraints,
    IReadOnlyDictionary<Type, Func<Control>> ViewFactories,
    IReadOnlyDictionary<Type, Type> ServiceMappings
) : IModuleManifest
{
    public virtual bool CanLoad() => true;
}