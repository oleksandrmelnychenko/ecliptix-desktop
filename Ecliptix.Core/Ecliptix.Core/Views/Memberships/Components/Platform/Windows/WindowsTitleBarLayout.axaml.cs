using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Ecliptix.Core.Views.Memberships.Components.Platform.Windows;

public partial class WindowsTitleBarLayout : UserControl
{
    private Button? _minimizeButton;
    private Button? _maximizeButton;
    private Button? _closeButton;

    private Path? _maximizeIcon;
    private ToolTip? _maximizeToolTip;

    private Window? _hostWindow;

    public WindowsTitleBarLayout()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _minimizeButton = this.FindControl<Button>("MinimizeButton");
        _maximizeButton = this.FindControl<Button>("MaximizeButton");
        _closeButton = this.FindControl<Button>("CloseButton");
        _maximizeIcon = this.FindControl<Path>("MaximizeIcon");
        _maximizeToolTip = this.FindControl<ToolTip>("MaximizeToolTip");


        _hostWindow = VisualRoot as Window;

        if (_minimizeButton != null)
        {
            _minimizeButton.Click += MinimizeWindow;
        }

        if (_maximizeButton != null)
        {
            _maximizeButton.Click += MaximizeWindow;
        }

        if (_closeButton != null)
        {
            _closeButton.Click += CloseWindow;
        }

        SubscribeToWindowState();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_minimizeButton != null)
        {
            _minimizeButton.Click -= MinimizeWindow;
        }

        if (_maximizeButton != null)
        {
            _maximizeButton.Click -= MaximizeWindow;
        }

        if (_closeButton != null)
        {
            _closeButton.Click -= CloseWindow;
        }

        _hostWindow = null; // Дозволяємо GC його зібрати
    }

    private void CloseWindow(object? sender, RoutedEventArgs e)
    {
        _hostWindow?.Close();
    }

    private void MaximizeWindow(object? sender, RoutedEventArgs e)
    {
        if (_hostWindow == null)
        {
            return;
        }

        _hostWindow.WindowState = _hostWindow.WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void MinimizeWindow(object? sender, RoutedEventArgs e)
    {
        if (_hostWindow == null)
        {
            return;
        }

        _hostWindow.WindowState = WindowState.Minimized;
    }


    private async void SubscribeToWindowState()
    {
        while (_hostWindow == null)
        {
            await Task.Delay(50);
            _hostWindow = VisualRoot as Window;
        }

        _hostWindow.GetObservable(Window.WindowStateProperty)
            .Subscribe( state =>
            {
                if (_maximizeIcon == null || _maximizeToolTip == null)
                {
                    return;
                }

                if (state == WindowState.Maximized)
                {
                    _maximizeIcon.Data = Geometry.Parse("M2048 1638h-410v410h-1638v-1638h410v-410h1638v1638zm-614-1024h-1229v1229h1229v-1229zm409-409h-1229v205h1024v1024h205v-1229z");
                    _maximizeToolTip.Content = "Restore Down";
                }
                else
                {
                    _maximizeIcon.Data = Geometry.Parse("M2048 2048v-2048h-2048v2048h2048zM1843 1843h-1638v-1638h1638v1638z");
                    _maximizeToolTip.Content = "Maximize";
                }
            });
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

