using Avalonia.Interactivity;

namespace Ecliptix.Core.Controls;

public class SecureKeyCharactersAddedEventArgs(RoutedEvent routedEvent, int index, string characters)
    : RoutedEventArgs(routedEvent)
{
    public int Index { get; } = index;
    public string Characters { get; } = characters;
}