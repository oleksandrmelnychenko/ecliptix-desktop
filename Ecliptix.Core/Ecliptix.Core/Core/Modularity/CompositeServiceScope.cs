using System;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Core.Modularity;

public class CompositeServiceScope : IServiceScope
{
    private readonly IServiceScope _moduleScope;
    private readonly IServiceScope _mainScope;
    private readonly CompositeServiceProvider _serviceProvider;
    private bool _disposed;

    public CompositeServiceScope(IServiceScope moduleScope, IServiceScope mainScope)
    {
        _moduleScope = moduleScope;
        _mainScope = mainScope;
        _serviceProvider = new CompositeServiceProvider(_moduleScope.ServiceProvider, _mainScope.ServiceProvider);
    }

    public IServiceProvider ServiceProvider => _serviceProvider;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _serviceProvider?.Dispose();
        _moduleScope?.Dispose();
        _mainScope?.Dispose();
    }
}

public class CompositeServiceProvider(IServiceProvider moduleProvider, IServiceProvider mainProvider)
    : IServiceProvider, IDisposable
{
    private bool _disposed;

    public object? GetService(Type serviceType)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CompositeServiceProvider));

        try
        {
            object? service = moduleProvider.GetService(serviceType);
            if (service != null)
                return service;
        }
        catch
        {
            // Fall through to main provider
        }

        return mainProvider.GetService(serviceType);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        if (moduleProvider is IDisposable moduleDisposable)
            moduleDisposable.Dispose();

        if (mainProvider is IDisposable mainDisposable)
            mainDisposable.Dispose();
    }
}