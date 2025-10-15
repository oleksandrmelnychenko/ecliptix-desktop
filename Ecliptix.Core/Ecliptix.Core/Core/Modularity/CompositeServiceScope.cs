using System;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Core.Modularity;

internal class CompositeServiceScope : IServiceScope
{
    private readonly ServiceProvider _moduleServiceProvider;
    private readonly IServiceScope _parentScope;
    private readonly CompositeServiceProvider _serviceProvider;
    private bool _disposed;

    public CompositeServiceScope(ServiceProvider moduleServiceProvider, IServiceScope parentScope)
    {
        _moduleServiceProvider = moduleServiceProvider ?? throw new ArgumentNullException(nameof(moduleServiceProvider));
        _parentScope = parentScope ?? throw new ArgumentNullException(nameof(parentScope));
        _serviceProvider = new CompositeServiceProvider(_moduleServiceProvider, _parentScope.ServiceProvider);
    }

    public IServiceProvider ServiceProvider => _serviceProvider;

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _serviceProvider?.Dispose();
        _moduleServiceProvider?.Dispose();
        _parentScope?.Dispose();
    }
}

internal class CompositeServiceProvider : IServiceProvider, IDisposable
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
        ObjectDisposedException.ThrowIf(_disposed, this);

        object? service = _moduleProvider.GetService(serviceType);
        return service ?? _mainProvider.GetService(serviceType);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
    }
}
