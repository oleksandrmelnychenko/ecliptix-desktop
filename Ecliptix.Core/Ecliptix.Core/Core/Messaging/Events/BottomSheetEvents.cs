using System;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.Messaging.Events;

public enum BottomSheetComponentType
{
    DetectedLocalization,
    Hidden
}

public sealed record BottomSheetChangedEvent
{
    public UserControl? Control { get; }
    public BottomSheetComponentType ComponentType { get; }
    public bool ShowScrim { get; }
    public DateTime Timestamp { get; }

    private BottomSheetChangedEvent(BottomSheetComponentType componentType, bool showScrim, UserControl? userControl)
    {
        ShowScrim = showScrim;
        ComponentType = componentType;
        Control = userControl;
        Timestamp = DateTime.UtcNow;
    }

    public static BottomSheetChangedEvent New(BottomSheetComponentType componentType, bool showScrim = true, UserControl? userControl = null) =>
        new(componentType, showScrim, userControl);
}