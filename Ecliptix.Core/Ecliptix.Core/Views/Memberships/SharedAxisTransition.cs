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
    private readonly TimeSpan _duration = TimeSpan.FromMilliseconds(300);

    private const double SlideDistance = 30.0;
    
    private const double TargetScale = 0.92;

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        List<Task> tasks = [];
        int direction = forward ? 1 : -1;
        SplineEasing easing = new(0.4, 0.0, 0.2, 1.0); 

        if (from != null)
        {
            from.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            GetOrAddTransform<TranslateTransform>(from);
            GetOrAddTransform<ScaleTransform>(from);
            
            var fromAnimation = new Animation
            {
                Duration = _duration,
                Easing = easing,
                FillMode = FillMode.Forward,
                Children =
                {
                    // --- Opacity (Fade Out) ---
                    // Fades out over the entire duration for a smoother cross-fade.
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(Visual.OpacityProperty, 1.0) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0), // Fades out over full duration
                        Setters = { new Setter(Visual.OpacityProperty, 0.0) }
                    },
                    // --- Transform (Slide and Scale) ---
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(TranslateTransform.XProperty, 0.0),
                            new Setter(ScaleTransform.ScaleXProperty, 1.0),
                            new Setter(ScaleTransform.ScaleYProperty, 1.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(TranslateTransform.XProperty, -direction * SlideDistance),
                            new Setter(ScaleTransform.ScaleXProperty, TargetScale),
                            new Setter(ScaleTransform.ScaleYProperty, TargetScale)
                        }
                    }
                }
            };
            tasks.Add(fromAnimation.RunAsync(from, cancellationToken));
        }

        // Animate the incoming view (if it exists)
        if (to != null)
        {
            to.Opacity = 0;
            to.IsVisible = true;
            to.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
            
            var toTranslate = GetOrAddTransform<TranslateTransform>(to);
            var toScale = GetOrAddTransform<ScaleTransform>(to);
            
            toTranslate.X = direction * SlideDistance;
            toScale.ScaleX = TargetScale;
            toScale.ScaleY = TargetScale;

            var toAnimation = new Animation
            {
                Duration = _duration,
                Easing = easing,
                FillMode = FillMode.Forward,
                Children =
                {
                    // --- Opacity (Cross-Fade In) ---
                    // **THE FIX IS HERE:** This now starts fading in from the very beginning.
                    new KeyFrame
                    {
                        Cue = new Cue(0), // <<<<<<<<<<<<<<<< CHANGED FROM 0.3 TO 0
                        Setters = { new Setter(Visual.OpacityProperty, 0.0) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters = { new Setter(Visual.OpacityProperty, 1.0) }
                    },
                    // --- Transform (Slide and Scale) ---
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(TranslateTransform.XProperty, direction * SlideDistance),
                            new Setter(ScaleTransform.ScaleXProperty, TargetScale),
                            new Setter(ScaleTransform.ScaleYProperty, TargetScale)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1.0),
                        Setters =
                        {
                            new Setter(TranslateTransform.XProperty, 0.0),
                            new Setter(ScaleTransform.ScaleXProperty, 1.0),
                            new Setter(ScaleTransform.ScaleYProperty, 1.0)
                        }
                    }
                }
            };
            tasks.Add(toAnimation.RunAsync(to, cancellationToken));
        }

        await Task.WhenAll(tasks);

        if (cancellationToken.IsCancellationRequested) return;

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
        foreach (var child in transformGroup.Children)
        {
            if (child is T existing) return existing;
        }
        var newTransform = new T();
        transformGroup.Children.Add(newTransform);
        return newTransform;
    }
}