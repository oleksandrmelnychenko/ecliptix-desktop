using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Views.Memberships.Components.Platform.OSX;

public sealed partial class MacosTitleBarLayout : UserControl, ITitleBar
{
    private Button? _minimizeButton;
    private Button? _maximizeButton;
    private Button? _closeButton;
    private Window? _hostWindow;
    private Border? _mainBorder;

    private MacosTitleBarButtons? _buttonsControl;

    public MacosTitleBarLayout()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _hostWindow = VisualRoot as Window;

        _buttonsControl = this.FindControl<MacosTitleBarButtons>("PART_WindowButtons");

        if (_buttonsControl != null)
        {
            _minimizeButton = _buttonsControl.FindControl<Button>("PART_Minimize");
            _maximizeButton = _buttonsControl.FindControl<Button>("PART_Maximize");
            _closeButton = _buttonsControl.FindControl<Button>("PART_Close");
        }


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

    public void CloseWindow(object? sender, RoutedEventArgs e) => _hostWindow?.Close();

    public void MaximizeWindow(object? sender, RoutedEventArgs e)
    {
        if (_hostWindow == null || _mainBorder == null)
        {
            return;
        }

        bool isCurrentlyFullScreen = _hostWindow.WindowState == WindowState.FullScreen;

        _hostWindow.WindowState = isCurrentlyFullScreen
            ? WindowState.Normal
            : WindowState.FullScreen;
        //
        // _mainBorder.CornerRadius = isCurrentlyFullScreen
        //     ? new CornerRadius(12)
        //     : new CornerRadius(0);
    }

    public void MinimizeWindow(object? sender, RoutedEventArgs e)
    {
        if (_hostWindow == null)
        {
            return;
        }

        _hostWindow.WindowState = WindowState.Minimized;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

