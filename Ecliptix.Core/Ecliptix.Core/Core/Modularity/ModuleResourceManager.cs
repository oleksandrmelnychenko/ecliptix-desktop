using System.Collections.Concurrent;
using Ecliptix.Core.Modularity.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Modularity.Modularity;

public sealed class ModuleServiceContext(IServiceProvider parentProvider)
{
    public T GetParentService<T>() where T : notnull => parentProvider.GetRequiredService<T>();
}

public class ModuleResourceManager(IServiceProvider serviceProvider,
    Action<ModuleServiceContext, IServiceCollection>? defaultServiceConfigurator = null) : IDisposable
{
    private readonly ConcurrentDictionary<string, IModuleScope> _moduleScopes = new();
    private readonly Action<ModuleServiceContext, IServiceCollection>? _defaultServiceConfigurator =
        defaultServiceConfigurator;
    private bool _disposed;

    public IModuleScope CreateModuleScope(string moduleName, Action<IServiceCollection>? configureServices = null)
    {
        IServiceScope serviceScope;

        bool needsCustomScope = configureServices != null || _defaultServiceConfigurator != null;
        if (needsCustomScope)
        {
            IServiceScope parentScope = serviceProvider.CreateScope();
            ServiceCollection moduleServices = new();

            ModuleServiceContext context = new(parentScope.ServiceProvider);
            moduleServices.AddSingleton(context);

            _defaultServiceConfigurator?.Invoke(context, moduleServices);
            configureServices?.Invoke(moduleServices);

            ServiceProvider moduleServiceProvider = moduleServices.BuildServiceProvider();
            serviceScope = new CompositeServiceScope(moduleServiceProvider, parentScope);
        }
        else
        {
            serviceScope = serviceProvider.CreateScope();
        }

        ModuleScope moduleScope = new(moduleName, serviceScope);

        if (_moduleScopes.TryAdd(moduleName, moduleScope))
        {
            return moduleScope;
        }

        moduleScope.Dispose();
        throw new InvalidOperationException($"Module scope for '{moduleName}' already exists");

    }

    public bool RemoveModuleScope(string moduleName)
    {
        if (_moduleScopes.TryRemove(moduleName, out IModuleScope? scope))
        {
            scope.Dispose();
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            foreach (KeyValuePair<string, IModuleScope> kvp in _moduleScopes)
            {
                try
                {
                    kvp.Value.Dispose();
                }
                catch
                {

                }
            }

            _moduleScopes.Clear();
        }

        _disposed = true;
    }
}
