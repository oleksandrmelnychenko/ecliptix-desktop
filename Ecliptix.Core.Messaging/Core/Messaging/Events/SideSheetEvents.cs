namespace Ecliptix.Core.Messaging.Core.Messaging.Events;



public sealed record SideSheetCommandEvent
{
    public AnimationType AnimationType { get; }
    public object? ViewModel { get; }
    public bool ShowScrim { get; }
    public bool IsDismissable { get; }
    public DateTime Timestamp { get; }

    private SideSheetCommandEvent(AnimationType animationType,
        object? viewModel, bool showScrim, bool isDismissable)
    {
        AnimationType = animationType;
        ViewModel = viewModel;
        ShowScrim = showScrim;
        IsDismissable = isDismissable;
        Timestamp = DateTime.UtcNow;
    }

    public static SideSheetCommandEvent Show(object? viewModel,
        bool showScrim, bool isDismissable) =>
        new(AnimationType.SHOW, viewModel, showScrim, isDismissable);

    public static SideSheetCommandEvent Hide() =>
        new(AnimationType.HIDE, null, false, true);
}

public sealed record SideSheetHiddenEvent
{
    public bool WasDismissedByUser { get; }
    public DateTime Timestamp { get; }

    private SideSheetHiddenEvent(bool wasDismissedByUser)
    {
        WasDismissedByUser = wasDismissedByUser;
        Timestamp = DateTime.UtcNow;
    }

    public static SideSheetHiddenEvent UserDismissed() => new(true);
}

public sealed record SideSheetAnimationCompleteEvent
{
    public AnimationType AnimationType { get; }
    public DateTime Timestamp { get; }

    private SideSheetAnimationCompleteEvent(AnimationType animationType)
    {
        AnimationType = animationType;
        Timestamp = DateTime.UtcNow;
    }

    public static SideSheetAnimationCompleteEvent ShowComplete() => new(AnimationType.SHOW);
    public static SideSheetAnimationCompleteEvent HideComplete() => new(AnimationType.HIDE);
}
