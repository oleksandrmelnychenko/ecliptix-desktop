using Avalonia.Interactivity;

namespace Ecliptix.Core.Controls;

/// <summary>
/// Provides data for the PasswordCharactersRemoved event.
/// Contains the starting index and the number of characters that were removed.
/// </summary>
public class PasswordCharactersRemovedEventArgs : RoutedEventArgs
{
    public int Index { get; }
    public int Count { get; }

    public PasswordCharactersRemovedEventArgs(RoutedEvent routedEvent, int index, int count) : base(routedEvent)
    {
        Index = index;
        Count = count;
    }
}