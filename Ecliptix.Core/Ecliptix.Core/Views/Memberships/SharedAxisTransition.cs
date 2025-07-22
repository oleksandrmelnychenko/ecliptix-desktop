using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Styling;

namespace Ecliptix.Core.Views.Memberships;

public class SharedAxisTransition : IPageTransition
{
    private readonly TimeSpan _duration = TimeSpan.FromMilliseconds(350);
    private const double SlideDistance = 30;
    private const double TargetScale = 0.95;
    private const double FadeOutAt = 0.6;
    private const double DelayIncomingAt = 0.2;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        List<Task> tasks = [];
        int direction = forward ? 1 : -1;

        if (from != null)
        {
            tasks.Add(AnimateOutgoingView(from, direction, cancellationToken));
        }

        if (to != null)
        {
            tasks.Add(AnimateIncomingView(to, direction, cancellationToken));
        }

        await Task.WhenAll(tasks);

        if (!cancellationToken.IsCancellationRequested)
        {
            CleanupTransforms(from, to);
        }
    }

    private Task AnimateOutgoingView(Visual view, int direction, CancellationToken cancellationToken)
    {
        SetupViewTransforms(view);

        Animation animation = new()
        {
            Duration = _duration,
            Easing = new CubicEaseIn(),
            FillMode = FillMode.Forward,
            Children =
            {
                CreateKeyFrame(0, opacity: 1.0, translateX: 0.0, scaleX: 1.0, scaleY: 1.0),
                CreateKeyFrame(FadeOutAt, opacity: 0.0),
                CreateKeyFrame(1.0,
                    opacity: 0.0,
                    translateX: -direction * SlideDistance,
                    scaleX: TargetScale,
                    scaleY: TargetScale)
            }
        };

        return animation.RunAsync(view, cancellationToken);
    }

    private Task AnimateIncomingView(Visual view, int direction, CancellationToken cancellationToken)
    {
        PrepareIncomingView(view, direction);

        Animation animation = new()
        {
            Duration = _duration,
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                CreateKeyFrame(0, opacity: 0.0, translateX: direction * SlideDistance, scaleX: TargetScale,
                    scaleY: TargetScale),
                CreateKeyFrame(DelayIncomingAt, opacity: 0.0, translateX: direction * SlideDistance,
                    scaleX: TargetScale, scaleY: TargetScale),
                CreateKeyFrame(1.0, opacity: 1.0, translateX: 0.0, scaleX: 1.0, scaleY: 1.0)
            }
        };

        return animation.RunAsync(view, cancellationToken);
    }

    private static void SetupViewTransforms(Visual view)
    {
        view.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        GetOrAddTransform<TranslateTransform>(view);
        GetOrAddTransform<ScaleTransform>(view);
    }

    private static void PrepareIncomingView(Visual view, int direction)
    {
        view.Opacity = 0;
        view.IsVisible = true;
        view.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);

        TranslateTransform translateTransform = GetOrAddTransform<TranslateTransform>(view);
        ScaleTransform scaleTransform = GetOrAddTransform<ScaleTransform>(view);

        translateTransform.X = direction * SlideDistance;
        scaleTransform.ScaleX = TargetScale;
        scaleTransform.ScaleY = TargetScale;
    }

    private static KeyFrame CreateKeyFrame(double cue, double? opacity = null, double? translateX = null,
        double? scaleX = null, double? scaleY = null)
    {
        KeyFrame keyFrame = new() { Cue = new Cue(cue) };

        if (opacity.HasValue)
        {
            keyFrame.Setters.Add(new Setter(Visual.OpacityProperty, opacity.Value));
        }

        if (translateX.HasValue)
        {
            keyFrame.Setters.Add(new Setter(TranslateTransform.XProperty, translateX.Value));
        }

        if (scaleX.HasValue)
        {
            keyFrame.Setters.Add(new Setter(ScaleTransform.ScaleXProperty, scaleX.Value));
        }

        if (scaleY.HasValue)
        {
            keyFrame.Setters.Add(new Setter(ScaleTransform.ScaleYProperty, scaleY.Value));
        }

        return keyFrame;
    }

    private static void CleanupTransforms(Visual? from, Visual? to)
    {
        if (from != null)
        {
            from.IsVisible = false;
            from.RenderTransform = null;
            from.RenderTransformOrigin = RelativePoint.TopLeft;
        }

        if (to != null)
        {
            to.Opacity = 1.0;
            to.RenderTransform = null;
            to.RenderTransformOrigin = RelativePoint.TopLeft;
        }
    }

    private static T GetOrAddTransform<T>(Visual visual) where T : Transform, new()
    {
        if (visual.RenderTransform is not TransformGroup transformGroup)
        {
            transformGroup = new TransformGroup();
            if (visual.RenderTransform is Transform existingTransform)
            {
                transformGroup.Children.Add(existingTransform);
            }

            visual.RenderTransform = transformGroup;
        }

        foreach (Transform child in transformGroup.Children)
        {
            if (child is T existing)
            {
                return existing;
            }
        }

        T newTransform = new T();
        transformGroup.Children.Add(newTransform);
        return newTransform;
    }
}