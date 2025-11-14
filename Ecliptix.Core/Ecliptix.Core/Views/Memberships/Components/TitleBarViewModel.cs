using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Views.Memberships.Components;

public partial class TitleBarViewModel : ReactiveObject
{

    [Reactive] public bool DisableCloseButton { get; set; }
    [Reactive] public bool DisableMinimizeButton { get; set; }
    [Reactive] public bool DisableMaximizeButton { get; set; }

    [Reactive] public object? AccessoryViewModel { get; set; }

    [Reactive] public bool IsDragging { get; set; }
    [Reactive] public bool IsDraggingEnabled { get; set; } = true;

}
