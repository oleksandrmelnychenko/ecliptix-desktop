using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Memberships;
using ReactiveUI;

namespace Ecliptix.Core.Views.Memberships;

public partial class WelcomeView : ReactiveUserControl<WelcomeViewModel>
{
    private static readonly FrozenDictionary<string, object> PrecompiledResources = 
        new Dictionary<string, object>
        {
            ["ButtonHeight"] = 44.0,
            ["StandardFontSize"] = 14.0,
            ["LogoHeight"] = 36.0,
            ["TaglineHeight"] = 18.0,
            ["CircleSize"] = 180.0,
            ["InnerCircleSize"] = 170.0,
            ["HeaderSpacing"] = 12.0,
            ["ButtonSpacing"] = 8.0,
            ["SignInButtonCornerRadius"] = new CornerRadius(10),
            ["CreateAccountButtonCornerRadius"] = new CornerRadius(12),
            ["CircleImageCornerRadius"] = new CornerRadius(90),
            ["InnerCircleCornerRadius"] = new CornerRadius(85),
            ["GrayButtonHoverBrush"] = GrayButtonHoverBrush,
            ["GrayButtonPressedBrush"] = GrayButtonPressedBrush,
            ["DarkButtonHoverBrush"] = DarkButtonHoverBrush,
            ["DarkButtonPressedBrush"] = DarkButtonPressedBrush,
            ["CircleBorderColor"] = Colors.White,
            ["ButtonTransitionDuration"] = TimeSpan.FromMilliseconds(250),
            ["CircleBorderThickness"] = new Thickness(4),
            ["ContainerMargin"] = new Thickness(0, 8, 0, 8)
        }.ToFrozenDictionary();
    
    private static readonly SolidColorBrush GrayButtonHoverBrush = new(Color.Parse("#F5F5F5"));
    private static readonly SolidColorBrush GrayButtonPressedBrush = new(Color.Parse("#EEEEEE"));
    private static readonly SolidColorBrush DarkButtonHoverBrush = new(Color.Parse("#262626"));
    private static readonly SolidColorBrush DarkButtonPressedBrush = new(Color.Parse("#1f1f1f"));
    
    private bool _resourcesApplied;

    public WelcomeView()
    {
        ApplyPrecompiledResources();
        AvaloniaXamlLoader.Load(this);
        OptimizeRenderingPerformance();
    }
    
    private void ApplyPrecompiledResources()
    {
        if (_resourcesApplied) return;
        
        foreach ((string key, object value) in PrecompiledResources)
        {
            Resources[key] = value;
        }
        
        _resourcesApplied = true;
    }
    
    private void OptimizeRenderingPerformance()
    {
        ClipToBounds = true;
    }
}
