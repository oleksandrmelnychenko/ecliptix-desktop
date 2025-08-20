using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Modularity;
public class ModuleLoadingContext
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<IModule>> _loadingTasks = new();
    private readonly ConcurrentDictionary<string, IModule> _loadedModules = new();
    private readonly ConcurrentDictionary<string, DateTime> _loadTimes = new();
    private readonly SemaphoreSlim _loadingSemaphore;

    public ModuleLoadingContext(int maxConcurrentLoads = 4)
    {
        _loadingSemaphore = new SemaphoreSlim(maxConcurrentLoads, maxConcurrentLoads);
    }

    
    
    
    public Task<IModule> GetOrCreateLoadingTask(string moduleName, Func<Task<IModule>> loadFactory)
    {
        TaskCompletionSource<IModule> tcs = _loadingTasks.GetOrAdd(moduleName, _ =>
        {
            TaskCompletionSource<IModule> newTcs = new();
            Task.Run(async () =>
            {
                try
                {
                    await _loadingSemaphore.WaitAsync();
                    try
                    {
                        if (_loadedModules.TryGetValue(moduleName, out IModule? cached))
                        {
                            newTcs.SetResult(cached);
                            return;
                        }

                        DateTime startTime = DateTime.UtcNow;
                        IModule module = await loadFactory();
                        
                        _loadedModules[moduleName] = module;
                        _loadTimes[moduleName] = DateTime.UtcNow;
                        
                        newTcs.SetResult(module);
                    }
                    finally
                    {
                        _loadingSemaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    newTcs.SetException(ex);
                }
            });
            return newTcs;
        });
        return tcs.Task;
    }

    
    
    
    public IModule? GetCachedModule(string moduleName)
    {
        return _loadedModules.TryGetValue(moduleName, out IModule? module) ? module : null;
    }

    
    
    
    public ModuleLoadingStats GetStats()
    {
        return new ModuleLoadingStats
        {
            LoadedModulesCount = _loadedModules.Count,
            ActiveLoadingTasks = _loadingTasks.Count(kvp => !kvp.Value.Task.IsCompleted),
            AverageLoadTime = _loadTimes.Count > 0 
                ? _loadTimes.Values.Select(t => (DateTime.UtcNow - t).TotalMilliseconds).Average()
                : 0
        };
    }

    public void Dispose()
    {
        _loadingSemaphore?.Dispose();
    }
}
public record ModuleLoadingStats
{
    public int LoadedModulesCount { get; init; }
    public int ActiveLoadingTasks { get; init; }
    public double AverageLoadTime { get; init; }
}