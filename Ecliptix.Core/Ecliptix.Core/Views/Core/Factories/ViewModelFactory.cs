using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Ecliptix.Core.Views.Core.Factories;

public sealed class ViewModelFactory(IServiceProvider serviceProvider) : IViewModelFactory, IDisposable
{
    private readonly List<IDisposable> _trackedDisposables = new();
    private readonly Lock _lock = new();
    private bool _isDisposed;

    public T Create<T>() where T : class
    {
        ThrowIfDisposed();

        T viewModel = serviceProvider.GetRequiredService<T>();

        if (viewModel is IDisposable disposable)
        {
            Track(disposable);
        }

        return viewModel;
    }

    public T Create<T>(params object[] parameters) where T : class
    {
        ThrowIfDisposed();

        T viewModel = ActivatorUtilities.CreateInstance<T>(serviceProvider, parameters);

        if (viewModel is IDisposable disposable)
        {
            Track(disposable);
        }

        return viewModel;
    }

    public void Track(IDisposable viewModel)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            _trackedDisposables.Add(viewModel);
        }
    }

    public void DisposeAll()
    {
        List<IDisposable> disposables;

        lock (_lock)
        {
            disposables = new List<IDisposable>(_trackedDisposables);
            _trackedDisposables.Clear();
        }

        for (int i = disposables.Count - 1; i >= 0; i--)
        {
            try
            {
                disposables[i]?.Dispose();
            }
            catch
            {
                // Suppress exceptions during cleanup
            }
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        DisposeAll();
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(nameof(ViewModelFactory));
        }
    }
}
