using Avalonia.Interactivity;
using Ecliptix.Core.Controls.Common;

namespace Ecliptix.Core.Controls.EventArgs;

public sealed class CharacterRejectedEventArgs(
    char rejectedCharacter,
    CharacterWarningType warningType,
    string input = "")
    : RoutedEventArgs
{
    public char RejectedCharacter { get; } = rejectedCharacter;
    public CharacterWarningType WarningType { get; } = warningType;
    public string Input { get; } = input;
}
