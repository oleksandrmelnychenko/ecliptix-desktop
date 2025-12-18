using System;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.Messaging.Events;



public enum AnimationType
{
    SHOW,
    HIDE
}

public sealed record BottomSheetCommandEvent
{
    public AnimationType AnimationType { get; }
    public object? ViewModel { get; }
    public bool ShowScrim { get; }
    public bool IsDismissable { get; }
    public DateTime Timestamp { get; }

    private BottomSheetCommandEvent(AnimationType animationType,
        object? viewModel, bool showScrim, bool isDismissable)
    {
        AnimationType = animationType;
        ViewModel = viewModel;
        ShowScrim = showScrim;
        IsDismissable = isDismissable;
        Timestamp = DateTime.UtcNow;
    }

    public static BottomSheetCommandEvent Show(object? viewModel,
        bool showScrim, bool isDismissable) =>
        new(AnimationType.SHOW, viewModel, showScrim, isDismissable);

    public static BottomSheetCommandEvent Hide() =>
        new(AnimationType.HIDE, null, false, true);
}

public sealed record BottomSheetHiddenEvent
{
    public bool WasDismissedByUser { get; }
    public DateTime Timestamp { get; }

    private BottomSheetHiddenEvent(bool wasDismissedByUser)
    {
        WasDismissedByUser = wasDismissedByUser;
        Timestamp = DateTime.UtcNow;
    }

    public static BottomSheetHiddenEvent UserDismissed() => new(true);
}

public sealed record BottomSheetAnimationCompleteEvent
{
    public AnimationType AnimationType { get; }
    public DateTime Timestamp { get; }

    private BottomSheetAnimationCompleteEvent(AnimationType animationType)
    {
        AnimationType = animationType;
        Timestamp = DateTime.UtcNow;
    }

    public static BottomSheetAnimationCompleteEvent ShowComplete() => new(AnimationType.SHOW);
    public static BottomSheetAnimationCompleteEvent HideComplete() => new(AnimationType.HIDE);
}
