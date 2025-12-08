using Avalonia.Interactivity;

namespace Ecliptix.Core.Views.Core.Components.TitleBar;

public interface ITitleBar
{
    void CloseWindow(object? sender, RoutedEventArgs e);
    void MaximizeWindow(object? sender, RoutedEventArgs e);
    void MinimizeWindow(object? sender, RoutedEventArgs e);
}
