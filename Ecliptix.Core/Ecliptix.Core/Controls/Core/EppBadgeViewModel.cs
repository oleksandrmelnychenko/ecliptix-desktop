using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Core;

public class EppBadgeViewModel: ReactiveObject
{
    [Reactive] public string Text { get; set; } = "Protected by EPP";
    [Reactive] public string HoverText { get; set; } = "Protected by Ecliptix Protection Protocol";
    public string Icon { get; } = "M12 1L3 5v6c0 5.55 3.84 10.74 9 12 5.16-1.26 9-6.45 9-12V5l-9-4z";
}
