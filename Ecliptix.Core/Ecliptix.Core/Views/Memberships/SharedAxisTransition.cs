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

/// <summary>
/// A page transition that combines a sliding motion with a pronounced fade and a dynamic scale effect.
/// The scale origin changes based on the transition direction to enhance the slide effect.
/// </summary>
public class SharedAxisTransition : IPageTransition
{
    private readonly TimeSpan _duration = TimeSpan.FromMilliseconds(350);
    private const double SlideDistance = 40; // Increased for a more pronounced effect
    private const double TargetScale = 0.95;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        List<Task> tasks = new List<Task>();
        int direction = forward ? 1 : -1;

        RelativePoint fromOrigin, toOrigin;
        if (forward)
        {
            fromOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative);
            toOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
        }
        else
        {
            fromOrigin = new RelativePoint(0, 0.5, RelativeUnit.Relative);
            toOrigin = new RelativePoint(1, 0.5, RelativeUnit.Relative);
        }

        if (from != null)
        {
            from.RenderTransformOrigin = fromOrigin;
            GetOrAddTransform<TranslateTransform>(from);
            GetOrAddTransform<ScaleTransform>(from);

            Animation fromAnimation = new Animation
            {
                Duration = _duration,
                Easing = new CubicEaseIn(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.XProperty, 0.0), new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0) } },
                    new KeyFrame { Cue = new Cue(0.6), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.XProperty, -direction * SlideDistance), new Setter(ScaleTransform.ScaleXProperty, TargetScale), new Setter(ScaleTransform.ScaleYProperty, TargetScale) } }
                }
            };

            tasks.Add(fromAnimation.RunAsync(from, cancellationToken));
        }

        if (to != null)
        {
            to.Opacity = 0;
            to.IsVisible = true;
            to.RenderTransformOrigin = toOrigin;
            
            TranslateTransform toTranslate = GetOrAddTransform<TranslateTransform>(to);
            ScaleTransform toScale = GetOrAddTransform<ScaleTransform>(to);
            
            toTranslate.X = direction * SlideDistance;
            toScale.ScaleX = TargetScale;
            toScale.ScaleY = TargetScale;
            
            Animation toAnimation = new Animation
            {
                Duration = _duration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.XProperty, direction * SlideDistance), new Setter(ScaleTransform.ScaleXProperty, TargetScale), new Setter(ScaleTransform.ScaleYProperty, TargetScale) } },
                    new KeyFrame { Cue = new Cue(0.2), Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.XProperty, direction * SlideDistance), new Setter(ScaleTransform.ScaleXProperty, TargetScale), new Setter(ScaleTransform.ScaleYProperty, TargetScale) } },
                    new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.XProperty, 0.0), new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0) } }
                }
            };

            tasks.Add(toAnimation.RunAsync(to, cancellationToken));
        }

        await Task.WhenAll(tasks);

        if (cancellationToken.IsCancellationRequested) return;

        CleanupTransforms(from, to);
    }

    private void CleanupTransforms(Visual? from, Visual? to)
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
            if (visual.RenderTransform is Transform existingConcreteTransform)
            {
                transformGroup.Children.Add(existingConcreteTransform);
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