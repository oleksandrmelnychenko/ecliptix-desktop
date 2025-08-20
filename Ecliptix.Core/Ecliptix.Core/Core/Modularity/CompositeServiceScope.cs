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
        _moduleScope = moduleScope ?? throw new ArgumentNullException(nameof(moduleScope));
        _mainScope = mainScope ?? throw new ArgumentNullException(nameof(mainScope));
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

public class CompositeServiceProvider : IServiceProvider, IDisposable
{
    private readonly IServiceProvider _moduleProvider;
    private readonly IServiceProvider _mainProvider;
    private bool _disposed;

    public CompositeServiceProvider(IServiceProvider moduleProvider, IServiceProvider mainProvider)
    {
        _moduleProvider = moduleProvider ?? throw new ArgumentNullException(nameof(moduleProvider));
        _mainProvider = mainProvider ?? throw new ArgumentNullException(nameof(mainProvider));
    }

    public object? GetService(Type serviceType)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(CompositeServiceProvider));

        try
        {
            object? service = _moduleProvider.GetService(serviceType);
            if (service != null)
                return service;
        }
        catch
        {
            // Fall through to main provider
        }

        return _mainProvider.GetService(serviceType);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        
        if (_moduleProvider is IDisposable moduleDisposable)
            moduleDisposable.Dispose();
            
        if (_mainProvider is IDisposable mainDisposable)
            mainDisposable.Dispose();
    }
}