using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Modularity;

public class ModuleLoadingContext
{
    private readonly record struct ModuleState(IModule Module, DateTime LoadTime);

    private readonly ConcurrentDictionary<string, TaskCompletionSource<IModule>> _loadingTasks = new();
    private readonly ConcurrentDictionary<string, ModuleState> _loadedModules = new();
    private readonly SemaphoreSlim _loadingSemaphore;

    public ModuleLoadingContext(int maxConcurrentLoads = 4)
    {
        _loadingSemaphore = new SemaphoreSlim(maxConcurrentLoads, maxConcurrentLoads);
    }

    public Task<IModule> GetOrCreateLoadingTask(string moduleName, Func<Task<IModule>> loadFactory)
    {
        if (_loadedModules.TryGetValue(moduleName, out ModuleState cached))
        {
            return Task.FromResult(cached.Module);
        }

        TaskCompletionSource<IModule> tcs = _loadingTasks.GetOrAdd(moduleName, _ =>
        {
            TaskCompletionSource<IModule> newTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

            ExecuteModuleLoad(moduleName, loadFactory, newTcs);

            return newTcs;
        });

        return tcs.Task;
    }

    private async void ExecuteModuleLoad(string moduleName, Func<Task<IModule>> loadFactory, TaskCompletionSource<IModule> tcs)
    {
        try
        {
            await _loadingSemaphore.WaitAsync();
            try
            {
                if (_loadedModules.TryGetValue(moduleName, out ModuleState cached))
                {
                    tcs.TrySetResult(cached.Module);
                    return;
                }

                IModule module = await loadFactory();

                ModuleState state = new(module, DateTime.UtcNow);
                _loadedModules[moduleName] = state;

                tcs.TrySetResult(module);
            }
            finally
            {
                _loadingSemaphore.Release();
                _loadingTasks.TryRemove(moduleName, out _);
            }
        }
        catch (Exception ex)
        {
            tcs.TrySetException(ex);
            _loadingTasks.TryRemove(moduleName, out _);
        }
    }

    public IModule? GetCachedModule(string moduleName)
    {
        return _loadedModules.TryGetValue(moduleName, out ModuleState state) ? state.Module : null;
    }

    public ModuleLoadingStats GetStats()
    {
        return new ModuleLoadingStats
        {
            LoadedModulesCount = _loadedModules.Count,
            ActiveLoadingTasks = _loadingTasks.Count,
            AverageLoadTime = _loadedModules.Count > 0
                ? _loadedModules.Values.Select(s => (DateTime.UtcNow - s.LoadTime).TotalMilliseconds).Average()
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
