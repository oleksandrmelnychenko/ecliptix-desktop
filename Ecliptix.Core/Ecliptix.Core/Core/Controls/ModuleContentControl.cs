using System;
using Avalonia;
using Avalonia.Controls;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Utilities;
using Serilog;
using Splat;

namespace Ecliptix.Core.Core.Controls;

public sealed class ModuleContentControl : ContentControl
{
    private readonly IModuleViewFactory? _moduleViewFactory;

    public static readonly StyledProperty<object?> ViewModelContentProperty =
        AvaloniaProperty.Register<ModuleContentControl, object?>(nameof(ViewModelContent));

    public object? ViewModelContent
    {
        get => GetValue(ViewModelContentProperty);
        set => SetValue(ViewModelContentProperty, value);
    }

    public ModuleContentControl()
    {
        try
        {
            _moduleViewFactory = Locator.Current?.GetService<IModuleViewFactory>();
            Log.Debug("[ModuleContentControl] IModuleViewFactory resolved: {HasFactory}", _moduleViewFactory != null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ModuleContentControl] Failed to resolve IModuleViewFactory");
            _moduleViewFactory = null;
        }
    }

    static ModuleContentControl()
    {
        ViewModelContentProperty.Changed.AddClassHandler<ModuleContentControl>((control, e) =>
            control.OnViewModelContentChanged(e.NewValue));
    }

    private void OnViewModelContentChanged(object? newViewModel)
    {
        Log.Information("[ModuleContentControl] ViewModelContent changed to: {Type}", newViewModel?.GetType().Name ?? "null");

        if (newViewModel == null)
        {
            Content = null;
            return;
        }

        TryCreateViewWithModuleFactory(newViewModel)
            .Or(() => TryCreateViewWithStaticMapper(newViewModel).ToOption())
            .Match(
                view =>
                {
                    Log.Information("[ModuleContentControl] ✅ View created: {ViewType} for {VmType}", view.GetType().Name, newViewModel.GetType().Name);
                    view.DataContext = newViewModel;
                    Content = view;
                },
                () =>
                {
                    Log.Warning("[ModuleContentControl] ❌ No view found for {VmType}, using fallback", newViewModel.GetType().Name);
                    Content = CreateFallbackView();
                });
    }

    private Option<Control> TryCreateViewWithModuleFactory(object viewModel)
    {
        if (_moduleViewFactory == null)
        {
            return Option<Control>.None;
        }

        try
        {
            Option<Control> result = _moduleViewFactory.CreateView(viewModel.GetType());
            return result;
        }
        catch
        {
            return Option<Control>.None;
        }
    }

    private static Control? TryCreateViewWithStaticMapper(object viewModel)
    {
        try
        {
            Control? result = StaticViewMapper.CreateView(viewModel.GetType());
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static Control CreateFallbackView()
    {
        return new TextBlock
        {
            Text = "No view registered for this ViewModel type",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
    }
}
