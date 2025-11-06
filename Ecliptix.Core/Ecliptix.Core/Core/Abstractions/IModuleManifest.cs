using System.Collections.Generic;

namespace Ecliptix.Core.Core.Abstractions;

public interface IModuleManifest
{
    int Priority { get; }
    ModuleLoadingStrategy LoadingStrategy { get; }

    IReadOnlyList<ModuleIdentifier> Dependencies { get; }
}

public record ModuleManifest(
    int Priority,
    ModuleLoadingStrategy LoadingStrategy,
    IReadOnlyList<ModuleIdentifier> Dependencies
) : IModuleManifest
{
    public virtual bool CanLoad() => true;
}
