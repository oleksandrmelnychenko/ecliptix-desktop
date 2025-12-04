using System;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.Messaging.Events;

public enum SideSheetComponentType
{
    SETTINGS_PANEL,
    FILTERS,
    DETAILS,
    DETECTED_LOCALIZATION,
    HIDDEN
}

public sealed record SideSheetCommandEvent
{
    public AnimationType AnimationType { get; }
    public SideSheetComponentType ComponentType { get; }
    public UserControl? Control { get; }
    public bool ShowScrim { get; }
    public bool IsDismissable { get; }
    public DateTime Timestamp { get; }

    private SideSheetCommandEvent(AnimationType animationType, SideSheetComponentType componentType,
        UserControl? control, bool showScrim, bool isDismissable)
    {
        AnimationType = animationType;
        ComponentType = componentType;
        Control = control;
        ShowScrim = showScrim;
        IsDismissable = isDismissable;
        Timestamp = DateTime.UtcNow;
    }

    public static SideSheetCommandEvent Show(SideSheetComponentType componentType, UserControl? control,
        bool showScrim, bool isDismissable) =>
        new(AnimationType.SHOW, componentType, control, showScrim, isDismissable);

    public static SideSheetCommandEvent Hide() =>
        new(AnimationType.HIDE, SideSheetComponentType.HIDDEN, null, false, true);
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
