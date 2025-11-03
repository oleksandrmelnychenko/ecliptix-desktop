using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;

namespace Ecliptix.Core.Core.Modularity;

internal class ParallelModuleLoader : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ModuleLoadingContext _loadingContext;
    private readonly SemaphoreSlim _batchSemaphore;
    private readonly int _maxParallelism;

    public ParallelModuleLoader(
        IServiceProvider serviceProvider,
        int maxParallelism = 0)
    {
        _serviceProvider = serviceProvider;
        _maxParallelism = maxParallelism > 0 ? maxParallelism : Environment.ProcessorCount;
        _loadingContext = new ModuleLoadingContext(_maxParallelism);
        _batchSemaphore = new SemaphoreSlim(1, 1);
    }

    public async Task<IReadOnlyList<IModule>> LoadModulesAsync(
        IEnumerable<IModule> modules,
        CancellationToken cancellationToken = default)
    {
        await _batchSemaphore.WaitAsync(cancellationToken);
        try
        {
            return await LoadModulesInternalAsync(modules.ToArray(), cancellationToken);
        }
        finally
        {
            _batchSemaphore.Release();
        }
    }

    private async Task<IReadOnlyList<IModule>> LoadModulesInternalAsync(
        IModule[] modules,
        CancellationToken cancellationToken)
    {
        Log.Information("Starting parallel loading of {Count} modules", modules.Length);

        DateTime startTime = DateTime.UtcNow;
        ModulePriorityQueue priorityQueue = new();
        priorityQueue.EnqueueModules(modules);

        List<IModule> loadedModules = new(modules.Length);
        List<Task<IModule>> currentBatch = new(_maxParallelism);

        while (priorityQueue.Count > 0 || currentBatch.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (currentBatch.Count < _maxParallelism && priorityQueue.Count > 0)
            {
                IModule? nextModule = priorityQueue.DequeueNext();
                if (nextModule == null)
                {
                    break;
                }

                Task<IModule> loadTask = LoadModuleWithContextAsync(nextModule, cancellationToken);
                currentBatch.Add(loadTask);

                if (Serilog.Log.IsEnabled(LogEventLevel.Debug))
                {
                    Log.Debug("Queued module {ModuleName} for loading (Priority: {Priority})",
                        nextModule.Id.ToName(), nextModule.Manifest.Priority);
                }
            }

            if (currentBatch.Count == 0)
            {
                throw new InvalidOperationException("Deadlock detected - no modules can be loaded due to circular dependencies");
            }

            Task<IModule> completedTask = await Task.WhenAny(currentBatch);
            currentBatch.Remove(completedTask);

            try
            {
                IModule completedModule = await completedTask;
                loadedModules.Add(completedModule);

                if (Serilog.Log.IsEnabled(LogEventLevel.Debug))
                {
                    Log.Debug("Successfully loaded module {ModuleName}", completedModule.Id.ToName());
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load module");
                throw;
            }
        }

        TimeSpan loadTime = DateTime.UtcNow - startTime;
        Log.Information("Completed loading {Count} modules in {LoadTime:F2}ms",
            loadedModules.Count, loadTime.TotalMilliseconds);

        return loadedModules.AsReadOnly();
    }

    private Task<IModule> LoadModuleWithContextAsync(IModule module, CancellationToken cancellationToken)
    {
        return _loadingContext.GetOrCreateLoadingTask(module.Id.ToName(), async () =>
        {
            Log.Debug("Loading module {ModuleName} (Strategy: {Strategy})",
                module.Id.ToName(), module.Manifest.LoadingStrategy);

            try
            {
                await module.LoadAsync(_serviceProvider);

                Log.Information("Module {ModuleName} loaded successfully", module.Id.ToName());
                return module;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load module {ModuleName}", module.Id.ToName());
                throw;
            }
        });
    }

    public ModuleLoadingStats GetLoadingStats() => _loadingContext.GetStats();

    public void PreloadModulesAsync(IEnumerable<IModule> modules)
    {
        IModule[] backgroundModules = modules
            .Where(m => m.Manifest.LoadingStrategy == ModuleLoadingStrategy.Background)
            .ToArray();

        if (backgroundModules.Length == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await LoadModulesAsync(backgroundModules);
                Log.Information("Background preloading completed for {Count} modules", backgroundModules.Length);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Background preloading failed");
            }
        }).ContinueWith(task =>
        {
            if (task.IsFaulted && task.Exception != null)
            {
                Log.Error(task.Exception.GetBaseException(), "Unhandled exception in background module preloading");
            }
        }, TaskContinuationOptions.OnlyOnFaulted);
    }

    public void Dispose()
    {
        _loadingContext?.Release();
        _batchSemaphore?.Dispose();
    }
}
