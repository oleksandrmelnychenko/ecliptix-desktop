using System;
using Avalonia.Animation;
using Avalonia.Media;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public static class DefaultBottomSheetVariables
{
    public const double MinHeight = 200.0;
    public const double MaxHeight = 600.0;
    
    public const double DefaultAnimationDuration = 420.0;
    public const double ContentDelayMs = 70.0;
    
    public const double StartOpacity = 0.0;
    public const double EndOpacity = 1.0;
    public const double ScaleStart = 0.96;
    public const double ScaleEnd = 1.0;
    public const double ScaleOvershoot = 1.01;
    public const double DefaultScrimOpacity = 0.3;
    
    public const double KeyframeStart = 0.0;
    public const double KeyframeMid = 0.65;
    public const double KeyframeEnd = 1.0;
    public const double VerticalOvershoot = -4.0;
    
    public static readonly SolidColorBrush ScrimBrush = new(Color.Parse("#000000"));
    public static readonly FillMode AnimationFillMode = FillMode.Forward;
    public static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(DefaultAnimationDuration);
    public static readonly TimeSpan ContentDelay = TimeSpan.FromMilliseconds(ContentDelayMs);
    
    
    public const bool DefaultIsDismissableOnScrimClick = true;
}