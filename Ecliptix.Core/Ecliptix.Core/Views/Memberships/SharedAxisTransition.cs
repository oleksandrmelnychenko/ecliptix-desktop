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

public sealed class SharedAxisTransition : IPageTransition
{
    // Seamless transition - no blinks
    private readonly TimeSpan _duration = TimeSpan.FromMilliseconds(300);
    private const double SlideDistance = 8;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        var tasks = new List<Task>();
        var direction = forward ? 1 : -1;

        // Start both animations simultaneously for seamless handoff
        if (from != null)
            tasks.Add(AnimateOutgoingView(from, direction, cancellationToken));

        if (to != null)
            tasks.Add(AnimateIncomingView(to, direction, cancellationToken));

        await Task.WhenAll(tasks);

        if (!cancellationToken.IsCancellationRequested)
            CleanupViews(from, to);
    }

    private Task AnimateOutgoingView(Visual view, int direction, CancellationToken cancellationToken)
    {
        SetupViewForAnimation(view);

        var animation = new Animation
        {
            Duration = _duration,
            Easing = new SineEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                // Smooth fade out with gentle movement
                CreateKeyFrame(0.0,
                    opacity: 1.0,
                    translateX: 0.0),

                CreateKeyFrame(0.5, // Fade out by middle of animation
                    opacity: 0.0,
                    translateX: -direction * SlideDistance),

                CreateKeyFrame(1.0,
                    opacity: 0.0,
                    translateX: -direction * SlideDistance)
            }
        };

        return animation.RunAsync(view, cancellationToken);
    }

    private Task AnimateIncomingView(Visual view, int direction, CancellationToken cancellationToken)
    {
        PrepareIncomingView(view, direction);

        var animation = new Animation
        {
            Duration = _duration,
            Easing = new SineEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                // Start invisible and off-screen
                CreateKeyFrame(0.0,
                    opacity: 0.0,
                    translateX: direction * SlideDistance),

                // Begin fade in from middle of animation (overlaps with fade out)
                CreateKeyFrame(0.5,
                    opacity: 0.0,
                    translateX: direction * SlideDistance * 0.5),

                // Smooth entrance to final position
                CreateKeyFrame(1.0,
                    opacity: 1.0,
                    translateX: 0.0)
            }
        };

        return animation.RunAsync(view, cancellationToken);
    }

    private static void SetupViewForAnimation(Visual view)
    {
        view.RenderTransformOrigin = RelativePoint.Center;
        EnsureTransforms(view);
    }

    private static void PrepareIncomingView(Visual view, int direction)
    {
        view.Opacity = 0;
        view.IsVisible = true;
        view.RenderTransformOrigin = RelativePoint.Center;

        var translateTransform = EnsureTransform<TranslateTransform>(view);
        translateTransform.X = direction * SlideDistance;
    }

    private static KeyFrame CreateKeyFrame(double cue,
        double? opacity = null,
        double? translateX = null)
    {
        var keyFrame = new KeyFrame { Cue = new Cue(cue) };

        if (opacity.HasValue)
            keyFrame.Setters.Add(new Setter(Visual.OpacityProperty, opacity.Value));

        if (translateX.HasValue)
            keyFrame.Setters.Add(new Setter(TranslateTransform.XProperty, translateX.Value));

        return keyFrame;
    }

    private static void CleanupViews(Visual? from, Visual? to)
    {
        if (from != null)
        {
            from.IsVisible = false;
            from.RenderTransform = null;
            from.RenderTransformOrigin = RelativePoint.TopLeft;
            from.Opacity = 1.0;
        }

        if (to != null)
        {
            to.Opacity = 1.0;
            to.RenderTransform = null;
            to.RenderTransformOrigin = RelativePoint.TopLeft;
        }
    }

    private static void EnsureTransforms(Visual visual)
    {
        EnsureTransform<TranslateTransform>(visual);
    }

    private static T EnsureTransform<T>(Visual visual) where T : Transform, new()
    {
        var transformGroup = visual.RenderTransform as TransformGroup;

        if (transformGroup == null)
        {
            transformGroup = new TransformGroup();
            if (visual.RenderTransform is Transform existingTransform)
                transformGroup.Children.Add(existingTransform);
            visual.RenderTransform = transformGroup;
        }

        foreach (var child in transformGroup.Children)
        {
            if (child is T existing) return existing;
        }

        var newTransform = new T();
        transformGroup.Children.Add(newTransform);
        return newTransform;
    }
}