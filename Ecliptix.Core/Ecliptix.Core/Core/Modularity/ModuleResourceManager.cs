using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ecliptix.Core.Core.Abstractions;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;
public class ModuleResourceManager : BackgroundService
{
    private readonly ConcurrentDictionary<string, IModuleScope> _moduleScopes = new();
    private readonly IServiceProvider _serviceProvider;

    private readonly Timer _resourceMonitorTimer;
    private const int MonitorIntervalMs = 30000;

    public ModuleResourceManager(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;

        _resourceMonitorTimer = new Timer(MonitorResources, null, MonitorIntervalMs, MonitorIntervalMs);
    }

    public IModuleScope CreateModuleScope(string moduleName, IModuleResourceConstraints constraints, Action<IServiceCollection>? configureServices = null)
    {
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name cannot be null or empty", nameof(moduleName));

        IServiceScope serviceScope;

        if (configureServices != null)
        {
            IServiceScope parentScope = _serviceProvider.CreateScope();
            ServiceCollection moduleServices = new();
            configureServices(moduleServices);
            ServiceProvider moduleServiceProvider = moduleServices.BuildServiceProvider();
            serviceScope = new CompositeServiceScope(moduleServiceProvider.CreateScope(), parentScope);
        }
        else
        {
            serviceScope = _serviceProvider.CreateScope();
        }

        ModuleScope moduleScope = new(moduleName, serviceScope, constraints);

        _moduleScopes.TryAdd(moduleName, moduleScope);

        Log.Information("Created module scope for {ModuleName} with constraints: MaxMemory={MaxMemoryMB}MB, MaxThreads={MaxThreads}",
            moduleName, constraints.MaxMemoryMB, constraints.MaxThreads);

        return moduleScope;
    }

    public void ValidateAllModuleResources()
    {
        foreach (IModuleScope scope in _moduleScopes.Values)
        {
            try
            {
                if (!scope.ValidateResourceUsage())
                {
                    Log.Warning("Module {ModuleName} violates resource constraints", scope.ModuleName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error validating resources for module {ModuleName}", scope.ModuleName);
            }
        }
    }

    public ModuleResourceSummary GetResourceSummary()
    {
        var moduleUsages = _moduleScopes.Values
            .Select(scope => new { scope.ModuleName, Usage = scope.GetResourceUsage() })
            .ToList();

        return new ModuleResourceSummary
        {
            TotalModules = _moduleScopes.Count,
            TotalMemoryMB = moduleUsages.Sum(m => m.Usage.MemoryUsageMB),
            TotalThreads = moduleUsages.Sum(m => m.Usage.ActiveThreads),
            ModuleUsages = moduleUsages.ToDictionary(m => m.ModuleName, m => m.Usage)
        };
    }

    private void MonitorResources(object? state)
    {
        try
        {
            ValidateAllModuleResources();


            ModuleResourceSummary summary = GetResourceSummary();
            Log.Debug("Resource Summary - Modules: {ModuleCount}, Total Memory: {TotalMemoryMB}MB, Total Threads: {TotalThreads}",
                summary.TotalModules, summary.TotalMemoryMB, summary.TotalThreads);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error during resource monitoring");
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("Module Resource Manager started");


        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _resourceMonitorTimer.Dispose();

        foreach (IModuleScope scope in _moduleScopes.Values)
        {
            scope.Dispose();
        }

        _moduleScopes.Clear();

        base.Dispose();
    }
}
public record ModuleResourceSummary
{
    public int TotalModules { get; init; }
    public long TotalMemoryMB { get; init; }
    public int TotalThreads { get; init; }
    public Dictionary<string, ModuleResourceUsage> ModuleUsages { get; init; } = new();
}