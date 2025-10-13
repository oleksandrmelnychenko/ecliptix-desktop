using System;

namespace Ecliptix.Core.Core.Abstractions;
public interface IModuleResourceConstraints
{
    long MaxMemoryMB { get; }

    int MaxThreads { get; }

    TimeSpan MaxExecutionTime { get; }

    int Priority { get; }

    bool CanUnload { get; }
}

public record ModuleResourceConstraints : IModuleResourceConstraints
{
    public long MaxMemoryMB { get; init; } = 0;
    public int MaxThreads { get; init; } = 0;
    public TimeSpan MaxExecutionTime { get; init; } = TimeSpan.Zero;
    public int Priority { get; init; } = 0;
    public bool CanUnload { get; init; } = true;

    public static ModuleResourceConstraints Default => new();
}
