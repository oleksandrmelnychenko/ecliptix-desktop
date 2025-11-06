using Avalonia.Interactivity;
using Ecliptix.Core.Controls.Common;

namespace Ecliptix.Core.Controls.EventArgs;

public sealed class CharacterRejectedEventArgs(
    CharacterWarningType warningType)
    : RoutedEventArgs
{
    public CharacterWarningType WarningType { get; } = warningType;
}
