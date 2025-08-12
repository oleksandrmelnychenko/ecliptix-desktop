using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Ecliptix.Core.Services;
using ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class DetectLanguageDialog : UserControl, IActivatableView, IDisposable
{
    private static class AnimationConstants
    {
        public const double AnimationWidth = 126.0;
        public const double AnimationHeight = 96.0;
        public const int LoadDelayMs = 50;
        public const int RepeatCount = 3;
        public const string AssetPath = "avares://Ecliptix.Core/Assets/language.gif";
    }

    private static class ResourceKeys
    {
        public const string ButtonHeight = nameof(ButtonHeight);
        public const string TitleFontSize = nameof(TitleFontSize); 
        public const string SubtitleFontSize = nameof(SubtitleFontSize);
        public const string ButtonTransitionDuration = nameof(ButtonTransitionDuration);
    }

    private static class TypeNames
    {
        public const string AnimatedImage = "AnimatedImage.Avalonia.Controls.AnimatedImage, AnimatedImage.Avalonia";
        public const string ImageBehavior = "AnimatedImage.Avalonia.ImageBehavior, AnimatedImage.Avalonia";
    }

    private static readonly FrozenDictionary<string, object> PrecompiledResources = 
        new Dictionary<string, object>
        {
            [ResourceKeys.ButtonHeight] = 44.0,
            [ResourceKeys.TitleFontSize] = 22.0,
            [ResourceKeys.SubtitleFontSize] = 17.0,
            [ResourceKeys.ButtonTransitionDuration] = TimeSpan.FromMilliseconds(250)
        }.ToFrozenDictionary();

    private readonly record struct AnimationState(Border? Container, bool IsLoaded, CancellationTokenSource? CancellationSource);

    private AnimationState _animationState = new(null, false, null);
    private CompositeDisposable? _activationDisposables;

    public ViewModelActivator Activator { get; } = new();

    public DetectLanguageDialog()
    {
        InitializeComponent();
        ApplyPrecompiledResources();
        InitializeAnimation();
    }

    public DetectLanguageDialog(ILocalizationService localizationService) : this()
    {
        SetLocalizationService(localizationService);
    }

    public void SetLocalizationService(ILocalizationService localizationService)
    {
        DataContext = new DetectLanguageDialogVideModel(localizationService);
        SetupReactiveBindings();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void InitializeAnimation()
    {
        Border? container = this.FindControl<Border>("AnimationContainer");
        _animationState = _animationState with { Container = container };
    }

    private void ApplyPrecompiledResources()
    {
        foreach ((string key, object value) in PrecompiledResources)
        {
            Resources[key] = value;
        }
    }

    private void SetupReactiveBindings()
    {
        this.WhenActivated(disposables =>
        {
            _activationDisposables = disposables;
            SetupAnimationLoading(disposables);
        });
    }

    private void SetupAnimationLoading(CompositeDisposable disposables)
    {
        Observable.Timer(TimeSpan.FromMilliseconds(AnimationConstants.LoadDelayMs), RxApp.MainThreadScheduler)
            .Where(_ => !_animationState.IsLoaded && _animationState.Container is not null)
            .SelectMany(_ => LoadAnimatedImageAsync())
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe()
            .DisposeWith(disposables);
    }

    private IObservable<Unit> LoadAnimatedImageAsync()
    {
        return Observable.FromAsync(async () =>
        {
            if (_animationState.IsLoaded || _animationState.Container is null) 
                return;

            CancellationTokenSource cancellationSource = new();
            _animationState = _animationState with { CancellationSource = cancellationSource };

            try
            {
                // UI objects must be created on the UI thread
                object animatedImage = CreateAnimatedImage();
                
                if (!cancellationSource.Token.IsCancellationRequested && _animationState.Container is not null)
                {
                    _animationState.Container.Child = (Control)animatedImage;
                    _animationState = _animationState with { IsLoaded = true };
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch
            {
                _animationState = _animationState with { IsLoaded = true };
            }
            finally
            {
                cancellationSource.Dispose();
                _animationState = _animationState with { CancellationSource = null };
            }
        });
    }

    private static object CreateAnimatedImage()
    {
        Type? animatedImageType = Type.GetType(TypeNames.AnimatedImage);
        if (animatedImageType is null)
        {
            return CreateFallbackImage();
        }

        object animatedImage = System.Activator.CreateInstance(animatedImageType) 
                               ?? throw new InvalidOperationException("Failed to create animated image instance");

        ConfigureAnimatedImageProperties(animatedImage, animatedImageType);
        ConfigureAnimationBehavior(animatedImage);

        return animatedImage;
    }

    private static Image CreateFallbackImage()
    {
        return new Image 
        { 
            Width = AnimationConstants.AnimationWidth, 
            Height = AnimationConstants.AnimationHeight 
        };
    }

    private static void ConfigureAnimatedImageProperties(object animatedImage, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type animatedImageType)
    {
        animatedImageType.GetProperty("Width")?.SetValue(animatedImage, AnimationConstants.AnimationWidth);
        animatedImageType.GetProperty("Height")?.SetValue(animatedImage, AnimationConstants.AnimationHeight);
    }

    private static void ConfigureAnimationBehavior(object animatedImage)
    {
        Type? imageBehaviorType = Type.GetType(TypeNames.ImageBehavior);
        if (imageBehaviorType is null) return;

        (object? repeatProperty, object? sourceProperty) = GetAnimationProperties(imageBehaviorType);
        if (repeatProperty is null || sourceProperty is null) return;

        Control control = (Control)animatedImage;
        control.SetValue((AvaloniaProperty)repeatProperty, AnimationConstants.RepeatCount);
        control.SetValue((AvaloniaProperty)sourceProperty, AnimationConstants.AssetPath);
    }

    private static (object? RepeatProperty, object? SourceProperty) GetAnimationProperties([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] Type imageBehaviorType)
    {
        object? repeatProperty = imageBehaviorType.GetField("RepeatBehaviorProperty")?.GetValue(null);
        object? sourceProperty = imageBehaviorType.GetField("AnimatedSourceProperty")?.GetValue(null);
        return (repeatProperty, sourceProperty);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        DisposeAnimation();
        base.OnDetachedFromVisualTree(e);
    }

    private void DisposeAnimation()
    {
        _animationState.CancellationSource?.Cancel();
        _animationState.CancellationSource?.Dispose();

        if (_animationState.Container?.Child is IDisposable disposableChild)
        {
            disposableChild.Dispose();
        }

        if (_animationState.Container is not null)
        {
            _animationState.Container.Child = null;
        }

        _animationState = new AnimationState(null, false, null);
    }

    public void Dispose()
    {
        DisposeAnimation();
        _activationDisposables?.Dispose();
        GC.SuppressFinalize(this);
    }
}