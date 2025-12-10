using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Ecliptix.Core.Views.Core.Models;

namespace Ecliptix.Core.Views.Core.Services;

public interface IWindowAnimationService
{
    Task AnimateWindowAsync(
        WindowAnimationState state,
        Action<double, Size, PixelPoint?> onProgress,
        CancellationToken cancellationToken = default);

    PixelPoint? CalculateTargetPosition(
        PixelPoint currentPosition,
        Size currentSize,
        Size targetSize,
        Rect screenWorkingArea);

    double EaseInOutCubic(double t);
}
