using ReactiveUI;
using System;
using Serilog;

namespace Ecliptix.Core.Core.MVVM;

public class ReactiveUiViewLocatorAdapter(Abstractions.IViewLocator moduleViewLocator) : IViewLocator
{
    public IViewFor? ResolveView<T>(T? viewModel, string? contract = null)
    {
        if (viewModel == null)
        {
            return null;
        }

        object? view = moduleViewLocator.ResolveView(viewModel);

        if (view is not IViewFor viewForInstance) return null;
        viewForInstance.ViewModel ??= viewModel;
        return viewForInstance;
    }
}