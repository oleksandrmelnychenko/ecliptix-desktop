using System;
using System.Collections.Generic;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModuleManifest
{
    int Priority { get; }
    ModuleLoadingStrategy LoadingStrategy { get; }

    IReadOnlyList<ModuleIdentifier> Dependencies { get; }

    IModuleResourceConstraints ResourceConstraints { get; }

    bool CanLoad();
}

public record ModuleManifest(
    Version Version,
    int Priority,
    ModuleLoadingStrategy LoadingStrategy,
    IReadOnlyList<ModuleIdentifier> Dependencies,
    IModuleResourceConstraints ResourceConstraints
) : IModuleManifest
{
    public virtual bool CanLoad() => true;
}
