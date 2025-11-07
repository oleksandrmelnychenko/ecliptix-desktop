using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Views.Memberships.Components;

public class TitleBarViewModel : ReactiveObject
{

    [Reactive] public bool DisableCloseButton { get; set; } = false;
    [Reactive] public bool DisableMinimizeButton { get; set; } = false;
    [Reactive] public bool DisableMaximizeButton { get; set; } = false;

}
