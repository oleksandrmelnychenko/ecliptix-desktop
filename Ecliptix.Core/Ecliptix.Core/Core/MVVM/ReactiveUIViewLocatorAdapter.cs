using System.Diagnostics.CodeAnalysis;
using Ecliptix.Utilities;
using ReactiveUI;

namespace Ecliptix.Core.Core.MVVM;

public class ReactiveUiViewLocatorAdapter(Abstractions.IViewLocator moduleViewLocator) : IViewLocator
{
    [UnconditionalSuppressMessage("Trimming", "IL2026",
        Justification = "ViewLocator is registered with explicit view/viewmodel mappings at startup")]
    public IViewFor? ResolveView<T>(T? viewModel, string? contract = null)
    {
        if (Equals(viewModel, default(T)))
        {
            return null;
        }

        Option<object> viewOption = moduleViewLocator.ResolveView(viewModel);

        if (!viewOption.IsSome)
        {
            return null;
        }

        object view = viewOption.Value!;

        if (view is not IViewFor viewForInstance)
        {
            return null;
        }

        viewForInstance.ViewModel ??= viewModel;
        return viewForInstance;
    }
}
