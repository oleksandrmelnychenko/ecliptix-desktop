using System;
using Avalonia.Animation;
using Avalonia.Media;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public static class DefaultBottomSheetVariables
{
    public const double MinHeight = 200.0;
    public const double MaxHeight = 600.0;
    
    public const double DefaultAnimationDuration = 280.0;
    public const double ContentDelayMs = 40.0;
    
    public const double StartOpacity = 0.0;
    public const double EndOpacity = 1.0;
    public const double ScaleStart = 0.98;
    public const double ScaleEnd = 1.0;
    public const double ScaleOvershoot = 1.005;
    public const double DefaultScrimOpacity = 0.15;
    
    public const double KeyframeStart = 0.0;
    public const double KeyframeMid = 0.7;
    public const double KeyframeEnd = 1.0;
    public const double VerticalOvershoot = -2.0;
    
    public static readonly SolidColorBrush ScrimBrush = new(Color.Parse("#000000"));
    public static readonly FillMode AnimationFillMode = FillMode.Forward;
    public static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(DefaultAnimationDuration);
    public static readonly TimeSpan ContentDelay = TimeSpan.FromMilliseconds(ContentDelayMs);
    
    
    public const bool DefaultIsDismissableOnScrimClick = true;
}