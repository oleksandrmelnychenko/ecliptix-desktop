using ReactiveUI;
using ReactiveUI.Fody.Helpers;

namespace Ecliptix.Core.Controls.Core;

public class NetworkBadgeViewModel: ReactiveObject
{
    [Reactive] public string Text { get; set; } = "Online";

    public string Icon { get; } =
        "M12.01 21.49L16.64 16.86C15.4 15.63 13.78 14.97 12.01 14.97C10.27 14.97 8.64 15.63 7.38 16.86L12.01 21.49ZM18.96 14.54L21.08 12.42C18.66 10.02 15.45 8.7 12.01 8.7C8.59 8.7 5.37 10.02 2.94 12.42L5.06 14.54C6.9 12.7 9.36 11.7 12.01 11.7C14.67 11.7 17.13 12.7 18.96 14.54ZM24 9.5L21.88 7.38C19.23 4.74 15.72 3.28 12.01 3.28C8.31 3.28 4.79 4.74 2.14 7.38L0.02 9.5C3.21 6.32 7.45 4.56 12.01 4.56C16.58 4.56 20.82 6.32 24 9.5Z";
}
