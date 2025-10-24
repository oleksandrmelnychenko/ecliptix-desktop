using System;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.Messaging.Events;

public enum BottomSheetComponentType
{
    DetectedLocalization,
    RedirectNotification,
    UserRequestError,
    Hidden
}

public enum AnimationType
{
    Show,
    Hide
}

public sealed record BottomSheetCommandEvent
{
    public AnimationType AnimationType { get; }
    public BottomSheetComponentType ComponentType { get; }
    public UserControl? Control { get; }
    public bool ShowScrim { get; }
    public bool IsDismissable { get; }
    public DateTime Timestamp { get; }

    private BottomSheetCommandEvent(AnimationType animationType, BottomSheetComponentType componentType, UserControl? control, bool showScrim, bool isDismissable)
    {
        AnimationType = animationType;
        ComponentType = componentType;
        Control = control;
        ShowScrim = showScrim;
        IsDismissable = isDismissable;
        Timestamp = DateTime.UtcNow;
    }

    public static BottomSheetCommandEvent Show(BottomSheetComponentType componentType, UserControl? control, bool showScrim, bool isDismissable) =>
        new(AnimationType.Show, componentType, control, showScrim, isDismissable);

    public static BottomSheetCommandEvent Hide() =>
        new(AnimationType.Hide, BottomSheetComponentType.Hidden, null, false, true);
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

    public static BottomSheetAnimationCompleteEvent ShowComplete() => new(AnimationType.Show);
    public static BottomSheetAnimationCompleteEvent HideComplete() => new(AnimationType.Hide);
}
