using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Core;

public class EppBadgeViewModel: ReactiveObject
{
    [Reactive] public string Text { get; set; } = "Protected by EPP";
    [Reactive] public string HoverText { get; set; } = "Protected by Ecliptix Protection Protocol";
    public string Icon { get; } = "M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z";
}
