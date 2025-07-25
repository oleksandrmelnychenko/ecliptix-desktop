using Avalonia.Interactivity;

namespace Ecliptix.Core.Controls;

/// <summary>
/// Provides data for the PasswordCharactersAdded event.
/// Contains the starting index and the characters that were added.
/// </summary>
public class PasswordCharactersAddedEventArgs : RoutedEventArgs
{
    public int Index { get; }
    public string Characters { get; }

    public PasswordCharactersAddedEventArgs(RoutedEvent routedEvent, int index, string characters) : base(routedEvent)
    {
        Index = index;
        Characters = characters;
    }
}