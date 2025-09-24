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

public sealed record BottomSheetChangedEvent
{
    public UserControl? Control { get; }
    public BottomSheetComponentType ComponentType { get; }
    public bool ShowScrim { get; }
    public DateTime Timestamp { get; }
    
    public bool IsDismissable { get; }

    private BottomSheetChangedEvent(BottomSheetComponentType componentType, bool showScrim, UserControl? userControl, bool isDismissable)
    {
        ShowScrim = showScrim;
        ComponentType = componentType;
        Control = userControl;
        IsDismissable = isDismissable;
        Timestamp = DateTime.UtcNow;
    }

    public static BottomSheetChangedEvent New(BottomSheetComponentType componentType, bool showScrim = true, UserControl? userControl = null, bool isDismissable = true) =>
        new(componentType, showScrim, userControl, isDismissable);
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
    public static BottomSheetHiddenEvent ProgrammaticallyHidden() => new(false);
}