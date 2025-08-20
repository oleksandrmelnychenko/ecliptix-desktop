using System;
using Microsoft.Extensions.Logging;

namespace Ecliptix.Core.Core.Abstractions;
public interface IModuleScope : IDisposable
{
    
    
    
    IServiceProvider ServiceProvider { get; }
    
    
    
    
    ILogger Logger { get; }
    
    
    
    
    IModuleResourceConstraints Constraints { get; }
    
    
    
    
    string ModuleName { get; }
    
    
    
    
    
    bool ValidateResourceUsage();
    
    
    
    
    ModuleResourceUsage GetResourceUsage();
}
public record ModuleResourceUsage
{
    public long MemoryUsageMB { get; init; }
    public int ActiveThreads { get; init; }
    public TimeSpan ExecutionTime { get; init; }
    public DateTime LastAccessed { get; init; }
}