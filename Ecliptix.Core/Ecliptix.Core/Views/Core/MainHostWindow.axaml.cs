using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Core;

namespace Ecliptix.Core.Views.Core;

public partial class MainHostWindow : Window
{
    public MainHostWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);
    }

    private void TitleBarArea_PointerPressed(object? sender, PointerPressedEventArgs e)
    {

        if (e.Source is Border && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);

        else if (e.Source is DockPanel && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }


    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }


    private void Resize_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (
            sender is Border border
            && border.Tag is WindowEdge edge
            && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
        )
            BeginResizeDrag(edge, e);
    }
}
