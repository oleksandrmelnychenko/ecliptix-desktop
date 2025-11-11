using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Views.Memberships.Components.Platform.OSX;

public partial class MacosTitleBarLayout : UserControl
{
    private Button? _minimizeButton;
    private Button? _maximizeButton;
    private Button? _closeButton;
    private Window? _hostWindow;
    private Border? _mainBorder;

    public MacosTitleBarLayout()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _hostWindow = VisualRoot as Window;

        _minimizeButton = this.FindControl<Button>("PART_Minimize");
        _maximizeButton = this.FindControl<Button>("PART_Maximize");
        _closeButton = this.FindControl<Button>("PART_Close");


        if (_hostWindow != null)
        {
            _mainBorder = _hostWindow.FindControl<Border>("MainBorder");
        }

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

        _hostWindow = null;
    }

    private void CloseWindow(object? sender, RoutedEventArgs e) => _hostWindow?.Close();

    private void MaximizeWindow(object? sender, RoutedEventArgs e)
    {
        if (_hostWindow == null || _mainBorder == null)
        {
            return;
        }

        bool isCurrentlyFullScreen = _hostWindow.WindowState == WindowState.FullScreen;

        _hostWindow.WindowState = isCurrentlyFullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;

        _mainBorder.CornerRadius = isCurrentlyFullScreen
            ? new CornerRadius(12)
            : new CornerRadius(0);
    }

    private void MinimizeWindow(object? sender, RoutedEventArgs e)
    {
        if (_hostWindow == null)
        {
            return;
        }

        _hostWindow.WindowState = WindowState.Minimized;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

