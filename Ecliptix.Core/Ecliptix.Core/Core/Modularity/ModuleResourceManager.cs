using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Ecliptix.Core.Core.Abstractions;
using Serilog;

namespace Ecliptix.Core.Core.Modularity;

public class ModuleResourceManager : IDisposable
{
    private readonly ConcurrentDictionary<string, IModuleScope> _moduleScopes = new();
    private readonly IServiceProvider _rootServiceProvider;
    private bool _disposed;

    public ModuleResourceManager(IServiceProvider serviceProvider)
    {
        _rootServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    }

    public IModuleScope CreateModuleScope(string moduleName, IModuleResourceConstraints constraints, Action<IServiceCollection>? configureServices = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name cannot be null or empty", nameof(moduleName));

        IServiceScope serviceScope;

        if (configureServices != null)
        {
            IServiceScope parentScope = _rootServiceProvider.CreateScope();
            ServiceCollection moduleServices = new();
            configureServices(moduleServices);

            ServiceProvider moduleServiceProvider = moduleServices.BuildServiceProvider();
            serviceScope = new CompositeServiceScope(moduleServiceProvider, parentScope);
        }
        else
        {
            serviceScope = _rootServiceProvider.CreateScope();
        }

        ModuleScope moduleScope = new(moduleName, serviceScope);

        if (!_moduleScopes.TryAdd(moduleName, moduleScope))
        {
            moduleScope.Dispose();
            throw new InvalidOperationException($"Module scope for '{moduleName}' already exists");
        }

        Log.Information("Created module scope for {ModuleName}", moduleName);

        return moduleScope;
    }

    public bool RemoveModuleScope(string moduleName)
    {
        if (_moduleScopes.TryRemove(moduleName, out IModuleScope? scope))
        {
            scope.Dispose();
            Log.Information("Removed module scope for {ModuleName}", moduleName);
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        foreach (KeyValuePair<string, IModuleScope> kvp in _moduleScopes)
        {
            try
            {
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error disposing module scope for {ModuleName}", kvp.Key);
            }
        }

        _moduleScopes.Clear();
    }
}
