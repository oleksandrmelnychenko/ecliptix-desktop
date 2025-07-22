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

    public async Task Start(Visual? from, Visual? to, bool forward, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        List<Task> tasks = new List<Task>();
        var direction = forward ? 1 : -1;

        // Handle outgoing view
        if (from != null)
        {
            var fromTransform = GetOrAddTranslateTransform(from);

            var fromAnimation = new Animation
            {
                Duration = _duration,
                Easing = new CubicEaseIn(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(Visual.OpacityProperty, 1.0),
                            new Setter(TranslateTransform.XProperty, 0.0)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(0.5),
                        Setters = { new Setter(Visual.OpacityProperty, 0.0) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(Visual.OpacityProperty, 0.0),
                            new Setter(TranslateTransform.XProperty, -direction * SlideDistance)
                        }
                    }
                }
            };

            tasks.Add(fromAnimation.RunAsync(from, cancellationToken));
        }

        // Handle incoming view
        if (to != null)
        {
            // Ensure the view starts invisible and positioned correctly
            to.Opacity = 0;
            to.IsVisible = true;

            var toTransform = GetOrAddTranslateTransform(to);
            toTransform.X = direction * SlideDistance; // Set initial position

            var toAnimation = new Animation
            {
                Duration = _duration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters =
                        {
                            new Setter(Visual.OpacityProperty, 0.0),
                            new Setter(TranslateTransform.XProperty, direction * SlideDistance)
                        }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(0.5),
                        Setters = { new Setter(Visual.OpacityProperty, 1.0) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters =
                        {
                            new Setter(Visual.OpacityProperty, 1.0),
                            new Setter(TranslateTransform.XProperty, 0.0)
                        }
                    }
                }
            };

            tasks.Add(toAnimation.RunAsync(to, cancellationToken));
        }

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (TaskCanceledException)
        {
            // Handle cancellation gracefully
            return;
        }

        if (cancellationToken.IsCancellationRequested) return;

        // Clean up transforms after animation completes
        CleanupTransforms(from, to);
    }

    private void CleanupTransforms(Visual? from, Visual? to)
    {
        // Hide the old view completely
        if (from != null)
        {
            from.IsVisible = false;
            ResetTransform(from);
        }

        // Ensure new view is properly positioned
        if (to != null)
        {
            to.IsVisible = true;
            to.Opacity = 1.0;
            ResetTransform(to);
        }
    }

    private void ResetTransform(Visual visual)
    {
        if (visual.RenderTransform is TranslateTransform translateTransform)
        {
            translateTransform.X = 0;
            translateTransform.Y = 0;
        }
        else if (visual.RenderTransform is TransformGroup group)
        {
            foreach (var child in group.Children)
            {
                if (child is TranslateTransform tt)
                {
                    tt.X = 0;
                    tt.Y = 0;
                    break;
                }
            }
        }
    }

    private static TranslateTransform GetOrAddTranslateTransform(Visual visual)
    {
        switch (visual.RenderTransform)
        {
            case null:
                var newTransform = new TranslateTransform();
                visual.RenderTransform = newTransform;
                return newTransform;

            case TranslateTransform translateTransform:
                return translateTransform;

            case TransformGroup group:
                foreach (var child in group.Children)
                {
                    if (child is TranslateTransform childTranslateTransform)
                        return childTranslateTransform;
                }

                var newTt = new TranslateTransform();
                group.Children.Add(newTt);
                return newTt;

            default:
                var newGroup = new TransformGroup();
                if (visual.RenderTransform is Transform existingTransform)
                {
                    newGroup.Children.Add(existingTransform);
                }
    
                var newTranslateTransform = new TranslateTransform();
                newGroup.Children.Add(newTranslateTransform);
                visual.RenderTransform = newGroup;
                return newTranslateTransform;

        }
    }
}