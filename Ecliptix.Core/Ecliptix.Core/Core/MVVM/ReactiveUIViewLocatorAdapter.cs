using ReactiveUI;
using System;

namespace Ecliptix.Core.Core.MVVM;

public class ReactiveUiViewLocatorAdapter(Abstractions.IViewLocator moduleViewLocator) : ReactiveUI.IViewLocator
{
    public IViewFor? ResolveView<T>(T? viewModel, string? contract = null)
    {
        Serilog.Log.Information("🔍 ReactiveUIViewLocatorAdapter.ResolveView called with ViewModel: {ViewModelType}, Contract: {Contract}",
            viewModel?.GetType().Name ?? "null", contract ?? "null");

        if (viewModel == null)
        {
            Serilog.Log.Warning("❌ ReactiveUIViewLocatorAdapter: ViewModel is null");
            return null;
        }

        try
        {
            Serilog.Log.Information("🔍 ReactiveUIViewLocatorAdapter: Calling _moduleViewLocator.ResolveView");
            object? view = moduleViewLocator.ResolveView(viewModel);

            Serilog.Log.Information("🔍 ReactiveUIViewLocatorAdapter: Got view: {ViewType}",
                view?.GetType().Name ?? "null");

            if (view is IViewFor viewForInstance)
            {
                viewForInstance.ViewModel ??= viewModel;
                Serilog.Log.Information("✅ ReactiveUIViewLocatorAdapter: Successfully resolved IViewFor");
                return viewForInstance;
            }

            Serilog.Log.Warning("⚠️ ReactiveUIViewLocatorAdapter: View is not IViewFor: {ViewType}",
                view?.GetType().Name ?? "null");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "❌ ReactiveUIViewLocatorAdapter: Failed to resolve view for {ViewModelType}", typeof(T).Name);
        }

        Serilog.Log.Warning("❌ ReactiveUIViewLocatorAdapter: Returning null");
        return null;
    }
}