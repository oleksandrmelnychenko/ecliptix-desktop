using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Layout;
using Avalonia.Media;

namespace Ecliptix.Core.Shared.Transitions.Page;

public class SharedAxisPageTransition : IPageTransition
{
    public TimeSpan Duration { get; set; }
    public Easing Easing { get; set; }
    public double SlideDistance { get; set; }
    public double FadeThreshold { get; set; } = 0.5;
    public Orientation Orientation { get; set; }

    public SharedAxisPageTransition() : this(TimeSpan.FromMilliseconds(300), Orientation.Horizontal)
    {
    }

    public SharedAxisPageTransition(TimeSpan duration, Orientation orientation = Orientation.Horizontal)
    {
        Duration = duration;
        Orientation = orientation;
        Easing = new SplineEasing(0.2, 0.0, 0, 1.0);
        SlideDistance = 90.0;
    }

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        List<Task> tasks = new List<Task>();
        double distance = SlideDistance;

        double fromDest = forward ? -distance : distance;
        double toStart = forward ? distance : -distance;

        if (from != null)
        {
            TranslateTransform transform = new TranslateTransform();
            from.RenderTransform = transform;

            tasks.Add(AnimateAsync(
                target: from,
                transform: transform,
                startPos: 0, endPos: fromDest,
                isExit: true,
                token: cancellationToken));
        }

        if (to != null)
        {
            to.IsVisible = true;
            TranslateTransform transform = new TranslateTransform();
            to.RenderTransform = transform;
            to.Opacity = 0;

            tasks.Add(AnimateAsync(
                target: to,
                transform: transform,
                startPos: toStart, endPos: 0,
                isExit: false,
                token: cancellationToken));
        }

        await Task.WhenAll(tasks);

        if (from != null)
        {
            from.IsVisible = false;
            from.RenderTransform = null;
            from.Opacity = 1.0;
        }

        if (to != null)
        {
            to.RenderTransform = null;
            to.Opacity = 1.0;
        }
    }

    private async Task AnimateAsync(
        Visual target,
        TranslateTransform transform,
        double startPos, double endPos,
        bool isExit,
        CancellationToken token)
    {
        DateTime startTime = DateTime.UtcNow;
        double totalMs = Duration.TotalMilliseconds;

        while (true)
        {
            if (token.IsCancellationRequested)
            {
                break;
            }

            double elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
            double progress = Math.Min(elapsed / totalMs, 1.0);

            double moveProgress = Easing.Ease(progress);
            double currentPos = startPos + (endPos - startPos) * moveProgress;

            if (Orientation == Orientation.Horizontal)
            {
                transform.X = currentPos;
            }
            else
            {
                transform.Y = currentPos;
            }

            if (isExit)
            {
                if (progress < FadeThreshold)
                {
                    double localProgress = progress / FadeThreshold;
                    target.Opacity = 1.0 - localProgress;
                }
                else
                {
                    target.Opacity = 0.0;
                }
            }
            else
            {
                if (progress < FadeThreshold)
                {
                    target.Opacity = 0.0;
                }
                else
                {
                    double localProgress = (progress - FadeThreshold) / (1.0 - FadeThreshold);
                    target.Opacity = localProgress;
                }
            }

            if (progress >= 1.0)
            {
                break;
            }

            await Task.Delay(16, token);
        }

        if (Orientation == Orientation.Horizontal)
        {
            transform.X = endPos;
        }
        else
        {
            transform.Y = endPos;
        }

        target.Opacity = isExit ? 0.0 : 1.0;
    }
}
