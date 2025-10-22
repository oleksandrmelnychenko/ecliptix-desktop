using System;
using System.Threading;
using Ecliptix.Core.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;

internal class ModuleScope : IModuleScope
{
    private readonly IServiceScope _serviceScope;
    private long _disposed;

    public ModuleScope(string moduleName, IServiceScope serviceScope)
    {
        ModuleName = moduleName;
        _serviceScope = serviceScope;
        ServiceProvider = serviceScope.ServiceProvider;

        Log.Debug("Module scope created for {ModuleName}", ModuleName);
    }

    public IServiceProvider ServiceProvider { get; }
    public string ModuleName { get; }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
            return;

        try
        {
            Log.Debug("Disposing module scope for {ModuleName}", ModuleName);
            _serviceScope.Dispose();
            Log.Debug("Module scope disposed for {ModuleName}", ModuleName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error disposing module scope for {ModuleName}", ModuleName);
        }
    }
}
