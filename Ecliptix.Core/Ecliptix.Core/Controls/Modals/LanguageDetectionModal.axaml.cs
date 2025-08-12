using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class LanguageDetectionModal : UserControl, IActivatableView
{
    private static readonly FrozenDictionary<string, object> PrecompiledResources = 
        new Dictionary<string, object>()
        {
            ["ButtonHeight"] = 44.0,
            ["TitleFontSize"] = 22.0,
            ["SubtitleFontSize"] = 17.0,
            ["ButtonTransitionDuration"] = TimeSpan.FromMilliseconds(250)
        }.ToFrozenDictionary();
        
    private readonly Border? _animationContainer;
    private bool _animationLoaded;
    
    public ViewModelActivator Activator { get; } = new();
    
    public LanguageDetectionModal(ILocalizationService localizationService)
    {
        InitializeComponent();
        DataContext = new LanguageDetectionViewModel(localizationService);
        
        ApplyPrecompiledResources();
        
        _animationContainer = this.FindControl<Border>("AnimationContainer");
        
        SetupLazyAnimationLoading();
    }
    
    private void ApplyPrecompiledResources()
    {
        foreach ((string key, object value) in PrecompiledResources)
        {
            Resources[key] = value;
        }
    }
    
    private void SetupLazyAnimationLoading()
    {
        this.WhenActivated(disposables =>
        {
            Observable.Timer(TimeSpan.FromMilliseconds(50), RxApp.MainThreadScheduler)
                .Where(_ => !_animationLoaded && _animationContainer != null)
                .Subscribe(_ => LoadAnimatedGif())
                .DisposeWith(disposables);
        });
    }
    
    private void LoadAnimatedGif()
    {
        if (_animationLoaded || _animationContainer == null) return;
        
        try
        {
            object animatedImage = CreateAnimatedImage();
            _animationContainer.Child = (Control)animatedImage;
            _animationLoaded = true;
        }
        catch
        {
            _animationLoaded = true;
        }
    }
    
    private static object CreateAnimatedImage()
    {
        Type? animatedImageType = Type.GetType("AnimatedImage.Avalonia.Controls.AnimatedImage, AnimatedImage.Avalonia");
        if (animatedImageType == null)
        {
            return new Image { Width = 126, Height = 96 };
        }
        
        object animatedImage = System.Activator.CreateInstance(animatedImageType) ?? throw new InvalidOperationException();
        
        animatedImageType.GetProperty("Width")?.SetValue(animatedImage, 126.0);
        animatedImageType.GetProperty("Height")?.SetValue(animatedImage, 96.0);
        
        Type? imageBehaviorType = Type.GetType("AnimatedImage.Avalonia.ImageBehavior, AnimatedImage.Avalonia");
        if (imageBehaviorType != null)
        {
            object? repeatProperty = imageBehaviorType.GetField("RepeatBehaviorProperty")?.GetValue(null);
            object? sourceProperty = imageBehaviorType.GetField("AnimatedSourceProperty")?.GetValue(null);
            
            if (repeatProperty != null && sourceProperty != null)
            {
                ((Control)animatedImage).SetValue((AvaloniaProperty)repeatProperty, 3);
                ((Control)animatedImage).SetValue((AvaloniaProperty)sourceProperty, "avares://Ecliptix.Core/Assets/language.gif");
            }
        }
        
        return animatedImage;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}