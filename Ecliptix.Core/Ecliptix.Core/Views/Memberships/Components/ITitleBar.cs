using Avalonia.Interactivity;

namespace Ecliptix.Core.Views.Memberships.Components;

public interface ITitleBar
{
    void CloseWindow(object? sender, RoutedEventArgs e);
    void MaximizeWindow(object? sender, RoutedEventArgs e);
    void MinimizeWindow(object? sender, RoutedEventArgs e);
}
