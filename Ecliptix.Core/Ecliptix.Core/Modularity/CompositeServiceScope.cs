using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Modularity;

internal class CompositeServiceScope : IServiceScope
{
    private readonly ServiceProvider _moduleServiceProvider;
    private readonly IServiceScope _parentScope;
    private readonly CompositeServiceProvider _serviceProvider;
    private bool _disposed;

    public CompositeServiceScope(ServiceProvider moduleServiceProvider, IServiceScope parentScope,
        IReadOnlyList<IServiceProvider> dependencyProviders)
    {
        _moduleServiceProvider = moduleServiceProvider;
        _parentScope = parentScope;
        _serviceProvider = new CompositeServiceProvider(_moduleServiceProvider, dependencyProviders, _parentScope.ServiceProvider);
    }

    public IServiceProvider ServiceProvider => _serviceProvider;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _moduleServiceProvider.Dispose();
        _parentScope.Dispose();
    }
}

internal class CompositeServiceProvider(
    IServiceProvider moduleProvider,
    IReadOnlyList<IServiceProvider> dependencyProviders,
    IServiceProvider mainProvider) : IServiceProvider
{
    public object? GetService(Type serviceType)
    {
        object? service = moduleProvider.GetService(serviceType);
        if (service != null)
        {
            return service;
        }

        foreach (IServiceProvider provider in dependencyProviders)
        {
            service = provider.GetService(serviceType);
            if (service != null)
            {
                return service;
            }
        }

        return mainProvider.GetService(serviceType);
    }
}
