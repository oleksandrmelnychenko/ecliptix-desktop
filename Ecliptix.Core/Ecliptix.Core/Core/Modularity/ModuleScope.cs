using System;
using System.Threading;
using Ecliptix.Core.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;

internal sealed class ModuleScope(string moduleName, IServiceScope serviceScope) : IModuleScope
{
    private long _disposed;

    public IServiceProvider ServiceProvider { get; } = serviceScope.ServiceProvider;
    public string ModuleName { get; } = moduleName;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 1)
        {
            return;
        }

        try
        {
            serviceScope.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ERROR disposing module scope for {ModuleName}", ModuleName);
        }
    }
}
