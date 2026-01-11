using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Modularity;

public sealed class ModuleServiceContext(IServiceProvider parentProvider)
{
    public T GetParentService<T>() where T : notnull => parentProvider.GetRequiredService<T>();
}

public sealed class ModuleResourceManager(IServiceProvider serviceProvider,
    Action<ModuleServiceContext, IServiceCollection>? defaultServiceConfigurator = null) : IDisposable
{
    private readonly ConcurrentDictionary<string, IModuleScope> _moduleScopes = new();
    private bool _disposed;

    public IModuleScope CreateModuleScope(string moduleName, IEnumerable<string>? dependencyModuleNames = null,
        Action<IServiceCollection>? configureServices = null)
    {
        IServiceScope serviceScope;

        bool hasDependencies = dependencyModuleNames != null && dependencyModuleNames.Any();
        bool needsCustomScope = configureServices != null || defaultServiceConfigurator != null || hasDependencies;
        if (needsCustomScope)
        {
            IServiceScope parentScope = serviceProvider.CreateScope();
            ServiceCollection moduleServices = new();

            ModuleServiceContext context = new(parentScope.ServiceProvider);
            moduleServices.AddSingleton(context);

            defaultServiceConfigurator?.Invoke(context, moduleServices);
            configureServices?.Invoke(moduleServices);

            ServiceProvider moduleServiceProvider = moduleServices.BuildServiceProvider();
            IReadOnlyList<IServiceProvider> dependencyProviders = ResolveDependencyProviders(dependencyModuleNames);
            serviceScope = new CompositeServiceScope(moduleServiceProvider, parentScope, dependencyProviders);
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

    private IReadOnlyList<IServiceProvider> ResolveDependencyProviders(IEnumerable<string>? dependencyModuleNames)
    {
        if (dependencyModuleNames == null)
        {
            return Array.Empty<IServiceProvider>();
        }

        List<IServiceProvider> providers = new();

        foreach (string moduleName in dependencyModuleNames)
        {
            if (_moduleScopes.TryGetValue(moduleName, out IModuleScope? scope))
            {
                providers.Add(scope.ServiceProvider);
            }
        }

        return providers;
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

    private void Dispose(bool disposing)
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
