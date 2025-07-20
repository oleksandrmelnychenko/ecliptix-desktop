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

        List<Task> tasks = [];
        int direction = forward ? 1 : -1;

        if (from != null)
        {
            GetOrAddTranslateTransform(from);
            
            Animation fromAnimation = new()
            {
                Duration = _duration,
                Easing = new CubicEaseIn(),
                FillMode = FillMode.Forward, 
                Children =
                {
                    new KeyFrame 
                    { 
                        Cue = new Cue(0), 
                        Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.XProperty, 0.0) } 
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(0.6),
                        Setters = { new Setter(Visual.OpacityProperty, 0.0) }
                    },
                    new KeyFrame 
                    { 
                        Cue = new Cue(1), 
                        Setters = 
                        { 
                            new Setter(Visual.OpacityProperty, 0.0), 
                            new Setter(TranslateTransform.XProperty, -direction * SlideDistance),
                            new Setter(Visual.IsVisibleProperty, false)
                        } 
                    }
                }
            };
            tasks.Add(fromAnimation.RunAsync(from, cancellationToken));
        }

        if (to != null)
        {
            to.Opacity = 0;
            to.IsVisible = true;
            GetOrAddTranslateTransform(to);

            Animation toAnimation = new()
            {
                Duration = _duration,
                Easing = new CubicEaseOut(),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame 
                    { 
                        Cue = new Cue(0), 
                        Setters = { new Setter(Visual.OpacityProperty, 0.0), new Setter(TranslateTransform.XProperty, direction * SlideDistance) } 
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(0.4),
                        Setters = { new Setter(Visual.OpacityProperty, 0.0) }
                    },
                    new KeyFrame 
                    { 
                        Cue = new Cue(1), 
                        Setters = { new Setter(Visual.OpacityProperty, 1.0), new Setter(TranslateTransform.XProperty, 0.0) } 
                    }
                }
            };
            tasks.Add(toAnimation.RunAsync(to, cancellationToken));
        }

        await Task.WhenAll(tasks);

        if (cancellationToken.IsCancellationRequested) return;

        if (from != null)
        {
            TranslateTransform finalFromTransform = GetOrAddTranslateTransform(from);
            finalFromTransform.X = 0;
            finalFromTransform.Y = 0;
        }
    }

    private static TranslateTransform GetOrAddTranslateTransform(Visual visual)
    {
        switch (visual.RenderTransform)
        {
            case TranslateTransform translateTransform:
                return translateTransform;
            case TransformGroup group:
            {
                foreach (Transform? child in group.Children)
                    if (child is TranslateTransform childTranslateTransform) return childTranslateTransform;
            
                TranslateTransform newTt = new();
                group.Children.Add(newTt);
                return newTt;
            }
        }

        TransformGroup newGroup = new();
        if (visual.RenderTransform is Transform existingTransform)
        {
            newGroup.Children.Add(existingTransform);
        }
        
        TranslateTransform newTranslateTransform = new();
        newGroup.Children.Add(newTranslateTransform);
        visual.RenderTransform = newGroup;
        return newTranslateTransform;
    }
}