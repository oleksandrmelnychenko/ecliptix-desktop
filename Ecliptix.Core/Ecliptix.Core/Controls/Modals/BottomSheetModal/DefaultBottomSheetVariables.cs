using System;
using Avalonia.Animation;
using Avalonia.Media;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public static class DefaultBottomSheetVariables
{
    // Core layout constants
    public const double MinHeight = 200.0;
    public const double MaxHeight = 600.0;
    
    // Ultra-smooth iOS-optimized animation constants  
    public const double DefaultAnimationDuration = 280.0; // Smoother timing
    public const double ContentDelayMs = 80.0; // Earlier content load for smoothness
    
    // Transform and opacity constants
    public const double StartOpacity = 0.0;
    public const double EndOpacity = 1.0;
    public const double ScaleStart = 0.96; // Gentler initial scale
    public const double ScaleEnd = 1.0;
    public const double ScaleOvershoot = 1.01; // More subtle overshoot
    public const double DefaultScrimOpacity = 0.04; // Even subtler scrim
    
    // Animation keyframe constants for ultra-smoothness
    public const double KeyframeStart = 0.0;
    public const double KeyframeMid = 0.65; // Smoother curve distribution
    public const double KeyframeEnd = 1.0;
    public const double VerticalOvershoot = -4.0; // Gentler bounce
    
    // Pre-allocated immutable resources
    public static readonly SolidColorBrush ScrimBrush = new(Color.Parse("#000000"));
    public static readonly FillMode AnimationFillMode = FillMode.Forward;
    public static readonly TimeSpan AnimationDuration = TimeSpan.FromMilliseconds(DefaultAnimationDuration);
    public static readonly TimeSpan ContentDelay = TimeSpan.FromMilliseconds(ContentDelayMs);
    
    // Static constructor removed - Avalonia doesn't support Freeze()
    
    // Eliminated redundant properties - use direct constants for better performance
    public const bool DefaultIsDismissableOnScrimClick = true;
}