using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Services;

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
        // Basic check to avoid dragging via buttons (might need refinement)
        if (e.Source is Border && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
        // Handle if source is DockPanel background etc. if needed
        else if (e.Source is DockPanel && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
    }

    // --- Traffic Light Buttons ---
    private void MinimizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    // --- *** Resizing Handler *** ---
    private void Resize_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border &&
            border.Tag is WindowEdge edge &&
            e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) // Only resize on left-click
            BeginResizeDrag(edge, e);
    }
}