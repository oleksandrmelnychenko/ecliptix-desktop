using System;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Ecliptix.Core.Shell.Constants;
using Ecliptix.Core.Shell.Models;

namespace Ecliptix.Core.Shell.Services;

public sealed class WindowAnimationService : IWindowAnimationService
{
    public async Task AnimateWindowAsync(
        WindowAnimationState state,
        Action<double, Size, PixelPoint?>? onProgress,
        CancellationToken cancellationToken = default)
    {
        if (state is { NeedsSizeChange: false, NeedsPositionChange: false })
        {
            onProgress?.Invoke(MainWindowConstants.Layout.ANIMATION_PROGRESS_COMPLETE,
                new Size(state.TargetWidth, state.TargetHeight),
                state.TargetPosition);
            return;
        }

        TaskCompletionSource<bool> tcs = new();

        await using CancellationTokenRegistration registration = cancellationToken.Register(() => tcs.TrySetCanceled());

        DispatcherTimer? timer = null;

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            timer = new DispatcherTimer(
                MainWindowConstants.TimeSpans.AnimationFrameInterval,
                DispatcherPriority.Render,
                (sender, e) =>
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        timer?.Stop();
                        tcs.TrySetCanceled();
                        return;
                    }

                    DateTime now = DateTime.UtcNow;
                    double elapsedMs = (now - state.StartTime).TotalMilliseconds;
                    double totalDurationMs = state.Duration.TotalMilliseconds;

                    double progress = Math.Min(MainWindowConstants.Layout.ANIMATION_PROGRESS_COMPLETE,
                        elapsedMs / totalDurationMs);
                    double easedProgress = EaseInOutCubic(progress);

                    double currentWidth = state.StartWidth + (state.TargetWidth - state.StartWidth) * easedProgress;
                    double currentHeight = state.StartHeight + (state.TargetHeight - state.StartHeight) * easedProgress;

                    PixelPoint? currentPosition = null;
                    if (state.TargetPosition.HasValue)
                    {
                        int currentX = (int)(state.StartPosition.X +
                            (state.TargetPosition.Value.X - state.StartPosition.X) * easedProgress);
                        int currentY = (int)(state.StartPosition.Y +
                            (state.TargetPosition.Value.Y - state.StartPosition.Y) * easedProgress);
                        currentPosition = new PixelPoint(currentX, currentY);
                    }

                    onProgress?.Invoke(progress, new Size(currentWidth, currentHeight), currentPosition);

                    if (!(progress >= MainWindowConstants.Layout.ANIMATION_PROGRESS_COMPLETE))
                    {
                        return;
                    }

                    timer?.Stop();
                    onProgress?.Invoke(MainWindowConstants.Layout.ANIMATION_PROGRESS_COMPLETE,
                        new Size(state.TargetWidth, state.TargetHeight),
                        state.TargetPosition);
                    tcs.TrySetResult(true);
                });

            timer.Start();
        });

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => timer?.Stop());
        }
    }

    public PixelPoint? CalculateTargetPosition(
        PixelPoint currentPosition,
        Size currentSize,
        Size targetSize,
        Rect screenWorkingArea)
    {
        double currentWindowCenterX = currentPosition.X + currentSize.Width / MainWindowConstants.Layout.CENTER_DIVISOR;
        double currentWindowCenterY = currentPosition.Y + currentSize.Height / MainWindowConstants.Layout.CENTER_DIVISOR;

        double targetX = currentWindowCenterX - targetSize.Width / MainWindowConstants.Layout.CENTER_DIVISOR;
        double targetY = currentWindowCenterY - targetSize.Height / MainWindowConstants.Layout.CENTER_DIVISOR;

        targetX = Math.Max(screenWorkingArea.X,
            Math.Min(targetX, screenWorkingArea.X + screenWorkingArea.Width - targetSize.Width));
        targetY = Math.Max(screenWorkingArea.Y,
            Math.Min(targetY, screenWorkingArea.Y + screenWorkingArea.Height - targetSize.Height));

        return new PixelPoint((int)targetX, (int)targetY);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double EaseInOutCubic(double t)
    {
        return t < MainWindowConstants.Layout.EASING_THRESHOLD
            ? 4 * t * t * t
            : 1 - Math.Pow(-2 * t + 2, 3) / MainWindowConstants.Layout.CENTER_DIVISOR;
    }
}
