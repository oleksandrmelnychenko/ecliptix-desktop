using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Styling;

namespace Ecliptix.Core.Views;

public static class AnimationExtensions
{
    public static Task FadeOutAsync(this Visual target, TimeSpan duration)
    {
        Animation animation = new()
        {
            Duration = duration,
            Easing = new CubicEaseInOut(), 
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } }
            }
        };
        return animation.RunAsync(target);
    }

    public static Task FadeInAsync(this Visual target, TimeSpan duration)
    {
        Animation animation = new()
        {
            Duration = duration,
            Easing = new SineEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(Visual.OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } }
            }
        };
        return animation.RunAsync(target);
    }
}