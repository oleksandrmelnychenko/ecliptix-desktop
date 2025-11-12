using ReactiveUI;
using ReactiveUI.SourceGenerators;

namespace Ecliptix.Core.Views.Memberships.Components;

public partial class TitleBarViewModel : ReactiveObject
{

    [Reactive] public bool DisableCloseButton { get; set; }
    [Reactive] public bool DisableMinimizeButton { get; set; }
    [Reactive] public bool DisableMaximizeButton { get; set; }

}
