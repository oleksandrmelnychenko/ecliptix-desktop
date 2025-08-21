using ReactiveUI;
using System;
using Serilog;

namespace Ecliptix.Core.Core.MVVM;

public class ReactiveUiViewLocatorAdapter(Abstractions.IViewLocator moduleViewLocator) : IViewLocator
{
    public IViewFor? ResolveView<T>(T? viewModel, string? contract = null)
    {
        Log.Information("🔍 ReactiveUIViewLocatorAdapter.ResolveView called with ViewModel: {ViewModelType}, Contract: {Contract}",
            viewModel?.GetType().Name ?? "null", contract ?? "null");

        if (viewModel == null)
        {
            Log.Warning("❌ ReactiveUIViewLocatorAdapter: ViewModel is null");
            return null;
        }

        try
        {
            Log.Information("🔍 ReactiveUIViewLocatorAdapter: Calling _moduleViewLocator.ResolveView");
            object? view = moduleViewLocator.ResolveView(viewModel);

            Log.Information("🔍 ReactiveUIViewLocatorAdapter: Got view: {ViewType}",
                view?.GetType().Name ?? "null");

            if (view is IViewFor viewForInstance)
            {
                viewForInstance.ViewModel ??= viewModel;
                Log.Information("✅ ReactiveUIViewLocatorAdapter: Successfully resolved IViewFor");
                return viewForInstance;
            }

            Log.Warning("⚠️ ReactiveUIViewLocatorAdapter: View is not IViewFor: {ViewType}",
                view?.GetType().Name ?? "null");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "❌ ReactiveUIViewLocatorAdapter: Failed to resolve view for {ViewModelType}", typeof(T).Name);
        }

        Log.Warning("❌ ReactiveUIViewLocatorAdapter: Returning null");
        return null;
    }
}