using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ecliptix.Core.Core.Abstractions;

namespace Ecliptix.Core.Core.Modularity;
public class ParallelModuleLoader : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ParallelModuleLoader> _logger;
    private readonly ModuleLoadingContext _loadingContext;
    private readonly SemaphoreSlim _batchSemaphore;
    private readonly int _maxParallelism;

    public ParallelModuleLoader(
        IServiceProvider serviceProvider,
        int maxParallelism = 0)
    {
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetService<ILogger<ParallelModuleLoader>>() ??
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ParallelModuleLoader>.Instance;
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
        _logger.LogInformation("Starting parallel loading of {Count} modules", modules.Length);

        DateTime startTime = DateTime.UtcNow;
        ModulePriorityQueue priorityQueue = new();
        priorityQueue.EnqueueModules(modules);

        List<IModule> loadedModules = new();
        List<Task> currentBatch = new();

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

                Task loadTask = LoadModuleWithRetryAsync(nextModule, cancellationToken);
                currentBatch.Add(loadTask);

                _logger.LogDebug("Queued module {ModuleName} for loading (Priority: {Priority})",
                    nextModule.Id.ToName(), nextModule.Manifest.Priority);
            }

            if (currentBatch.Count == 0)
            {
                throw new InvalidOperationException("Deadlock detected - no modules can be loaded due to circular dependencies");
            }


            Task completedTask = await Task.WhenAny(currentBatch);
            currentBatch.Remove(completedTask);

            try
            {
                await completedTask;
                if (completedTask is Task<IModule> moduleTask)
                {
                    IModule completedModule = await moduleTask;
                    loadedModules.Add(completedModule);

                    _logger.LogDebug("Successfully loaded module {ModuleName}", completedModule.Id.ToName());
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load module");
                throw;
            }
        }


        await Task.WhenAll(currentBatch);
        foreach (Task<IModule> task in currentBatch.OfType<Task<IModule>>())
        {
            loadedModules.Add(await task);
        }

        TimeSpan loadTime = DateTime.UtcNow - startTime;
        _logger.LogInformation("Completed loading {Count} modules in {LoadTime:F2}ms",
            loadedModules.Count, loadTime.TotalMilliseconds);

        return loadedModules.AsReadOnly();
    }

    private async Task<IModule> LoadModuleWithRetryAsync(IModule module, CancellationToken cancellationToken)
    {
        return await _loadingContext.GetOrCreateLoadingTask(module.Id.ToName(), async () =>
        {
            _logger.LogDebug("Loading module {ModuleName} (Strategy: {Strategy})",
                module.Id.ToName(), module.Manifest.LoadingStrategy);

            try
            {
                await module.LoadAsync(_serviceProvider);

                _logger.LogInformation("Module {ModuleName} loaded successfully", module.Id.ToName());
                return module;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load module {ModuleName}", module.Id.ToName());
                throw;
            }
        });
    }




    public ModuleLoadingStats GetLoadingStats()
    {
        return _loadingContext.GetStats();
    }




    public void PreloadModulesAsync(IEnumerable<IModule> modules)
    {
        IModule[] backgroundModules = modules
            .Where(m => m.Manifest.LoadingStrategy == ModuleLoadingStrategy.Background)
            .ToArray();

        if (backgroundModules.Length == 0) return;

        _ = Task.Run(async () =>
        {
            try
            {
                await LoadModulesAsync(backgroundModules);
                _logger.LogInformation("Background preloading completed for {Count} modules", backgroundModules.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Background preloading failed");
            }
        });
    }

    public void Dispose()
    {
        _loadingContext?.Dispose();
        _batchSemaphore?.Dispose();
    }
}