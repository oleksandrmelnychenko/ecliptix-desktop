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

    public static ModuleResourceConstraints Critical => new()
    {
        MaxMemoryMB = 0,
        MaxThreads = 0,
        Priority = 100,
        CanUnload = false
    };

    public static ModuleResourceConstraints Limited => new()
    {
        MaxMemoryMB = 100,
        MaxThreads = 5,
        MaxExecutionTime = TimeSpan.FromSeconds(30),
        Priority = 10,
        CanUnload = true
    };
}