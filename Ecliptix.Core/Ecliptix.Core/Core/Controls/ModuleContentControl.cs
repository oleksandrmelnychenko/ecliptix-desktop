using Avalonia;
using Avalonia.Controls;
using Ecliptix.Core.Core.MVVM;
using Ecliptix.Core.Modularity.Abstractions;
using Ecliptix.Utilities;
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
        }
        catch
        {
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
                    view.DataContext = newViewModel;
                    Content = view;
                },
                () =>
                {
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
