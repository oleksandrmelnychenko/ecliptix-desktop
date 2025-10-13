using Avalonia.Interactivity;

namespace Ecliptix.Core.Controls.EventArgs;

public class SecureKeyCharactersRemovedEventArgs(RoutedEvent routedEvent, int index, int count)
    : RoutedEventArgs(routedEvent)
{
    public int Index { get; } = index;
    public int Count { get; } = count;
}
