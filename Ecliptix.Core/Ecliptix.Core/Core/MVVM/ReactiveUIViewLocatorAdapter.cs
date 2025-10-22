using ReactiveUI;
using System;
using System.Diagnostics.CodeAnalysis;
using Serilog;

namespace Ecliptix.Core.Core.MVVM;

public class ReactiveUiViewLocatorAdapter(Abstractions.IViewLocator moduleViewLocator) : IViewLocator
{
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ViewLocator is registered with explicit view/viewmodel mappings at startup")]
    public IViewFor? ResolveView<T>(T? viewModel, string? contract = null)
    {
        if (viewModel == null)
        {
            return null;
        }

        object? view = moduleViewLocator.ResolveView(viewModel);

        if (view is not IViewFor viewForInstance)
        {
            return null;
        }

        viewForInstance.ViewModel ??= viewModel;
        return viewForInstance;
    }
}
