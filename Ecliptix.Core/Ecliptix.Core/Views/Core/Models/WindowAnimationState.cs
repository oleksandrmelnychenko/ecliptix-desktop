using System;
using Avalonia;

namespace Ecliptix.Core.Views.Core.Models;

public readonly struct WindowAnimationState(
    double startWidth,
    double startHeight,
    PixelPoint startPosition,
    double targetWidth,
    double targetHeight,
    PixelPoint? targetPosition,
    DateTime startTime,
    TimeSpan duration)
{
    public readonly double StartWidth = startWidth;
    public readonly double StartHeight = startHeight;
    public readonly PixelPoint StartPosition = startPosition;
    public readonly double TargetWidth = targetWidth;
    public readonly double TargetHeight = targetHeight;
    public readonly PixelPoint? TargetPosition = targetPosition;
    public readonly DateTime StartTime = startTime;
    public readonly TimeSpan Duration = duration;

    public bool NeedsSizeChange => Math.Abs(StartWidth - TargetWidth) >= 0.01 ||
                                   Math.Abs(StartHeight - TargetHeight) >= 0.01;

    public bool NeedsPositionChange => TargetPosition.HasValue &&
                                       TargetPosition.Value != StartPosition;
}
