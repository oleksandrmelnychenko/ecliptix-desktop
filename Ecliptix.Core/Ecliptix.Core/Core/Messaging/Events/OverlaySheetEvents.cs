using System;
using Avalonia.Controls;

namespace Ecliptix.Core.Core.Messaging.Events;

public enum OverlayAnimationType
{
    SHOW,
    HIDE
}

public sealed record OverlaySheetCommandEvent
{
    public OverlayAnimationType AnimationType { get; }
    public object? ViewModel { get; }
    public bool ShowScrim { get; }
    public bool IsDismissable { get; }
    public DateTime Timestamp { get; }

    private OverlaySheetCommandEvent(OverlayAnimationType animationType,
        object? viewModel, bool showScrim, bool isDismissable)
    {
        AnimationType = animationType;
        ViewModel = viewModel;
        ShowScrim = showScrim;
        IsDismissable = isDismissable;
        Timestamp = DateTime.UtcNow;
    }

    public static OverlaySheetCommandEvent Show(object? viewModel,
        bool showScrim, bool isDismissable) =>
        new(OverlayAnimationType.SHOW, viewModel, showScrim, isDismissable);

    public static OverlaySheetCommandEvent Hide() =>
        new(OverlayAnimationType.HIDE, null, false, true);
}

public sealed record OverlaySheetHiddenEvent
{
    public DateTime Timestamp { get; }

    private OverlaySheetHiddenEvent()
    {
        Timestamp = DateTime.UtcNow;
    }

    public static OverlaySheetHiddenEvent Create() => new();
}

public sealed record OverlaySheetAnimationCompleteEvent
{
    public OverlayAnimationType AnimationType { get; }
    public DateTime Timestamp { get; }

    private OverlaySheetAnimationCompleteEvent(OverlayAnimationType animationType)
    {
        AnimationType = animationType;
        Timestamp = DateTime.UtcNow;
    }

    public static OverlaySheetAnimationCompleteEvent ShowComplete() => new(OverlayAnimationType.SHOW);
    public static OverlaySheetAnimationCompleteEvent HideComplete() => new(OverlayAnimationType.HIDE);
}
