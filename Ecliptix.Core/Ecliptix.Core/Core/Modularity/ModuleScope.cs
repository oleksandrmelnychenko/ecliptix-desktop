using System;
using System.Diagnostics;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Ecliptix.Core.Core.Abstractions;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;
public class ModuleScope : IModuleScope
{
    private readonly IServiceScope _serviceScope;
    private readonly Stopwatch _executionTimer;
    private long _disposed;

    public IServiceProvider ServiceProvider { get; }
    public IModuleResourceConstraints Constraints { get; }
    public string ModuleName { get; }

    public ModuleScope(
        string moduleName,
        IServiceScope serviceScope,
        IModuleResourceConstraints constraints)
    {
        ModuleName = moduleName ?? throw new ArgumentNullException(nameof(moduleName));
        _serviceScope = serviceScope ?? throw new ArgumentNullException(nameof(serviceScope));
        Constraints = constraints ?? throw new ArgumentNullException(nameof(constraints));

        ServiceProvider = serviceScope.ServiceProvider;
        _executionTimer = Stopwatch.StartNew();

        Log.Debug("Module scope created for {ModuleName}", ModuleName);
    }

    public bool ValidateResourceUsage()
    {
        ModuleResourceUsage usage = GetResourceUsage();


        if (Constraints.MaxMemoryMB > 0 && usage.MemoryUsageMB > Constraints.MaxMemoryMB)
        {
            Log.Warning("Module {ModuleName} exceeds memory limit: {UsageMB}MB > {LimitMB}MB",
                ModuleName, usage.MemoryUsageMB, Constraints.MaxMemoryMB);
            return false;
        }


        if (Constraints.MaxThreads > 0 && usage.ActiveThreads > Constraints.MaxThreads)
        {
            Log.Warning("Module {ModuleName} exceeds thread limit: {UsageThreads} > {LimitThreads}",
                ModuleName, usage.ActiveThreads, Constraints.MaxThreads);
            return false;
        }


        if (Constraints.MaxExecutionTime != TimeSpan.Zero && usage.ExecutionTime > Constraints.MaxExecutionTime)
        {
            Log.Warning("Module {ModuleName} exceeds execution time limit: {UsageTime} > {LimitTime}",
                ModuleName, usage.ExecutionTime, Constraints.MaxExecutionTime);
            return false;
        }

        return true;
    }

    public ModuleResourceUsage GetResourceUsage()
    {
        long memoryUsage = 0;
        int activeThreads = 0;

        try
        {


            using Process currentProcess = Process.GetCurrentProcess();
            memoryUsage = currentProcess.WorkingSet64 / 1024 / 1024;



            activeThreads = currentProcess.Threads.Count;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to get resource usage for module {ModuleName}", ModuleName);
        }

        return new ModuleResourceUsage
        {
            MemoryUsageMB = memoryUsage,
            ActiveThreads = activeThreads,
            ExecutionTime = _executionTimer.Elapsed,
            LastAccessed = DateTime.UtcNow
        };
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
            return;

        try
        {
            Log.Debug("Disposing module scope for {ModuleName}", ModuleName);

            _executionTimer?.Stop();
            _serviceScope?.Dispose();

            Log.Debug("Module scope disposed for {ModuleName}", ModuleName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing module scope for {ModuleName}", ModuleName);
        }
    }
}