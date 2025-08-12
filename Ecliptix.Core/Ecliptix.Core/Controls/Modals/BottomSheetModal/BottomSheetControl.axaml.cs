using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using ReactiveUI;
using Serilog;
using Splat;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public partial class BottomSheetControl : ReactiveUserControl<BottomSheetViewModel>
{
    private bool _contentLoaded;
    private object? _pendingContent;

    private double _sheetHeight;
    private Border? _sheetBorder;
    private Border? _scrimBorder;
    private ContentControl? _contentControl;

    private Animation? _showAnimation;
    private Animation? _hideAnimation;
    private Animation? _scrimShowAnimation;
    private Animation? _scrimHideAnimation;
    private bool _isAnimating;

    public static readonly StyledProperty<double> AppearVerticalOffsetProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(AppearVerticalOffset));

    public static readonly StyledProperty<double> DisappearVerticalOffsetProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(DisappearVerticalOffset));

    public new static readonly StyledProperty<double> MinHeightProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(MinHeight), DefaultBottomSheetVariables.MinHeight);

    public new static readonly StyledProperty<double> MaxHeightProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(MaxHeight), DefaultBottomSheetVariables.MaxHeight);

    public static readonly StyledProperty<IBrush> ScrimColorProperty =
        AvaloniaProperty.Register<BottomSheetControl, IBrush>(nameof(ScrimColor),
            DefaultBottomSheetVariables.ScrimBrush);

    public static readonly StyledProperty<bool> IsDismissableOnScrimClickProperty =
        AvaloniaProperty.Register<BottomSheetControl, bool>(nameof(IsDismissableOnScrimClick),
            DefaultBottomSheetVariables.DefaultIsDismissableOnScrimClick);

    public static readonly StyledProperty<IBrush> DismissableScrimColorProperty =
        AvaloniaProperty.Register<BottomSheetControl, IBrush>(nameof(DismissableScrimColor),
            DefaultBottomSheetVariables.ScrimBrush);

    public static readonly StyledProperty<IBrush> UnDismissableScrimColorProperty =
        AvaloniaProperty.Register<BottomSheetControl, IBrush>(nameof(UnDismissableScrimColor),
            DefaultBottomSheetVariables.ScrimBrush);

    public IBrush DismissableScrimColor
    {
        get => GetValue(DismissableScrimColorProperty);
        set => SetValue(DismissableScrimColorProperty, value);
    }

    public IBrush UnDismissableScrimColor
    {
        get => GetValue(UnDismissableScrimColorProperty);
        set => SetValue(UnDismissableScrimColorProperty, value);
    }
    
    public double AppearVerticalOffset
    {
        get => GetValue(AppearVerticalOffsetProperty);
        set => SetValue(AppearVerticalOffsetProperty, value);
    }

    public double DisappearVerticalOffset
    {
        get => GetValue(DisappearVerticalOffsetProperty);
        set => SetValue(DisappearVerticalOffsetProperty, value);
    }

    public new double MinHeight
    {
        get => GetValue(MinHeightProperty);
        set => SetValue(MinHeightProperty, value);
    }

    public new double MaxHeight
    {
        get => GetValue(MaxHeightProperty);
        set => SetValue(MaxHeightProperty, value);
    }

    public IBrush ScrimColor
    {
        get => GetValue(ScrimColorProperty);
        set => SetValue(ScrimColorProperty, value);
    }

    public bool IsDismissableOnScrimClick
    {
        get => GetValue(IsDismissableOnScrimClickProperty);
        set => SetValue(IsDismissableOnScrimClickProperty, value);
    }

    public BottomSheetControl()
    {
        InitializeComponent();
        ViewModel = Locator.Current.GetService<BottomSheetViewModel>();
        IsVisible = false;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeControls();

        this.WhenActivated(disposables =>
        {

            SetupContentObservables(disposables);
            SetupVisibilityObservable(disposables);
            SetupDismissableCommand(disposables);
            SetupScrimColorObservable(disposables);
        });
    }

    private void InitializeControls()
    {
        _sheetBorder = this.FindControl<Border>("SheetBorder");
        _scrimBorder = this.FindControl<Border>("ScrimBorder");
        _contentControl = this.FindControl<ContentControl>("ContentControl");
    }

    private void SetupContentObservables(CompositeDisposable disposables)
    {
        if (_contentControl == null)
        {
            return;
        }
        
        _contentControl.GetObservable(ContentControl.ContentProperty)
            .StartWith(_contentControl.Content)
            .Where(content => content != null) 
            .SelectMany(content =>
            {
                return Observable.CombineLatest(
                    _contentControl.GetObservable(BoundsProperty).StartWith(_contentControl.Bounds).Take(1),
                    _contentControl.GetObservable(MarginProperty).StartWith(_contentControl.Margin).Take(1),
                    (bounds, margin) => new { Content = content, Bounds = bounds, Margin = margin });
            })
            .Throttle(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler)
            .Where(_ => !_isAnimating)
            .Subscribe(state =>
            {
                Log.Debug($"ContentObservables triggered: Content={state.Content}, Bounds={state.Bounds}, Margin={state.Margin}");
                UpdateSheetHeight();
                if (_showAnimation == null) CreateAnimations();
            })
            .DisposeWith(disposables);
    }

    private void SetupScrimColorObservable(CompositeDisposable disposables)
    {
        this.WhenAnyValue(
                x => x.IsDismissableOnScrimClick, 
                x => x.DismissableScrimColor,
                x => x.UnDismissableScrimColor)
            .Subscribe(tuple =>
            {
                (bool isDismissable, IBrush dismissableColor, IBrush unDismissableColor) = tuple;
                ScrimColor = isDismissable ? dismissableColor : unDismissableColor;
            })
            .DisposeWith(disposables);
    }
    
   private void SetupVisibilityObservable(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.ViewModel!.IsVisible, x => x.ViewModel!.ShowScrim)
            .Buffer(2, 1)
            .Select(b => (Previous: b[0], Current: b.Count > 1 ? b[1] : b[0]))
            .DistinctUntilChanged()
            .Where(_ => !_isAnimating)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async states =>
            {
                var (isVisible, showScrim) = states.Current;
                var (wasVisible, wasScrimShowing) = states.Previous;

                if (_sheetBorder == null || _scrimBorder == null)
                {
                    IsVisible = isVisible;
                    return;
                }
                
                if (isVisible && !wasVisible)
                {
                    if (_isAnimating) return;
                    
                    _isAnimating = true;
                    await ShowWithIOSStyleAnimation(showScrim);
                    _isAnimating = false;
                }
                else if (isVisible && showScrim && !wasScrimShowing)
                {
                    if (_scrimShowAnimation != null)
                    {
                        SetupScrimForAnimation(_scrimBorder);
                        await _scrimShowAnimation.RunAsync(_scrimBorder, CancellationToken.None);
                    }
                }
                else if (!isVisible && wasVisible)
                {
                    if (_isAnimating) return;
                    
                    _isAnimating = true;
                    await HideWithIOSStyleAnimation();
                    _isAnimating = false;
                }
            })
            .DisposeWith(disposables);
    }

    private void SetupDismissableCommand(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.ViewModel)
            .Where(vm => vm != null)
            .Take(1)
            .Subscribe(viewModel => { viewModel!.IsDismissableOnScrimClick = IsDismissableOnScrimClick; })
            .DisposeWith(disposables);

        this.WhenAnyValue(x => x.ViewModel!.IsDismissableOnScrimClick)
            .Subscribe(isDismissable => { IsDismissableOnScrimClick = isDismissable; })
            .DisposeWith(disposables);
    }

    private void UpdateSheetHeight()
    {
        if (_contentControl == null || _sheetBorder == null)
        {
            _sheetHeight = MinHeight;
            _sheetBorder.Height = _sheetHeight;
            return;
        }

        // Ensure the ContentControl is measured with appropriate constraints
        var availableSize = new Size(_contentControl.Bounds.Width > 0 ? _contentControl.Bounds.Width : double.PositiveInfinity, double.PositiveInfinity);
        _contentControl.Measure(availableSize);

        // Invalidate measure if necessary to ensure up-to-date size
        if (!_contentControl.IsMeasureValid)
        {
            _contentControl.InvalidateMeasure();
            _contentControl.Measure(availableSize);
        }

        // Calculate height including margins
        double verticalMargin = _contentControl.Margin.Top + _contentControl.Margin.Bottom;
        double contentHeight = _contentControl.DesiredSize.Height + verticalMargin;

        // Fallback to MinHeight if content height is invalid
        if (double.IsNaN(contentHeight) || contentHeight <= 0)
        {
            contentHeight = MinHeight;
        }

        // Clamp the height between MinHeight and MaxHeight
        _sheetHeight = Math.Clamp(contentHeight, MinHeight, MaxHeight);
        _sheetBorder.Height = _sheetHeight;

        // Log for debugging (optional)
        Log.Debug($"UpdateSheetHeight: ContentHeight={contentHeight}, SheetHeight={_sheetHeight}");
    }

    private void CreateAnimations()
    {
        double hiddenPosition = _sheetHeight + DisappearVerticalOffset;

        _showAnimation ??= CreateOptimizedShowAnimation(hiddenPosition);

        _hideAnimation ??= CreateOptimizedHideAnimation(hiddenPosition);

        _scrimShowAnimation ??= CreateOptimizedScrimShowAnimation();
        _scrimHideAnimation ??= CreateOptimizedScrimHideAnimation();
    }

    private async Task ShowWithIOSStyleAnimation(bool showScrim)
    {
        CreateAnimations();
        if (_showAnimation == null || _scrimShowAnimation == null)
        {
            IsVisible = true;
            return;
        }

        // Phase 1: Show container without content (iOS style)
        IsVisible = true;
        SetupViewForAnimation(_sheetBorder!);
        
        // Defer content loading
        _pendingContent = _contentControl?.Content;
        if (_contentControl != null) _contentControl.Content = null;
        
        List<Task> showTasks = [_showAnimation.RunAsync(_sheetBorder!, CancellationToken.None)];

        if (showScrim)
        {
            SetupScrimForAnimation(_scrimBorder!);
            showTasks.Add(_scrimShowAnimation.RunAsync(_scrimBorder!, CancellationToken.None));
        }
        else
        {
            _scrimBorder!.IsVisible = false;
        }

        // Start animation and content loading in parallel (iOS timing)
        Task animationTask = Task.WhenAll(showTasks);
        Task contentTask = LoadContentAfterDelay();
        
        await Task.WhenAll(animationTask, contentTask);
    }
    
    private async Task HideWithIOSStyleAnimation()
    {
        if (_hideAnimation == null || _scrimHideAnimation == null)
        {
            IsVisible = false;
            return;
        }

        List<Task> hideTasks = [_hideAnimation.RunAsync(_sheetBorder!, CancellationToken.None)];

        if (_scrimBorder!.IsVisible)
        {
            hideTasks.Add(_scrimHideAnimation.RunAsync(_scrimBorder, CancellationToken.None));
        }

        await Task.WhenAll(hideTasks);

        IsVisible = false;
        _scrimBorder.IsVisible = false;
        _contentLoaded = false;
    }
    
    private async Task LoadContentAfterDelay()
    {
        if (_contentLoaded || _pendingContent == null || _contentControl == null) return;
        
        // iOS-style delay: load content after animation starts
        await Task.Delay(DefaultBottomSheetVariables.ContentDelay, CancellationToken.None);
        
        _contentControl.Content = _pendingContent;
        _contentLoaded = true;
        _pendingContent = null;
    }
    
    private void SetupViewForAnimation(Visual view)
    {
        view.RenderTransformOrigin = RelativePoint.Center;
        TranslateTransform translateTransform = EnsureTransform<TranslateTransform>(view);
        ScaleTransform scaleTransform = EnsureTransform<ScaleTransform>(view);
        
        translateTransform.Y = _sheetHeight + DisappearVerticalOffset;
        scaleTransform.ScaleX = DefaultBottomSheetVariables.ScaleStart;
        scaleTransform.ScaleY = DefaultBottomSheetVariables.ScaleStart;
        
        view.Opacity = DefaultBottomSheetVariables.StartOpacity;
        view.IsVisible = true;
    }
    
    private static Animation CreateOptimizedShowAnimation(double hiddenPosition)
    {
        return new Animation
        {
            Duration = DefaultBottomSheetVariables.AnimationDuration,
            Easing = new SpringEasing(),
            FillMode = DefaultBottomSheetVariables.AnimationFillMode,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeStart),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.StartOpacity),
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeMid),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, DefaultBottomSheetVariables.VerticalOvershoot),
                        new Setter(OpacityProperty, 0.98), // Smoother opacity transition
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleOvershoot),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleOvershoot)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeEnd),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, 0.0),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.EndOpacity),
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd)
                    }
                }
            }
        };
    }
    
    private static Animation CreateOptimizedHideAnimation(double hiddenPosition)
    {
        return new Animation
        {
            Duration = DefaultBottomSheetVariables.AnimationDuration,
            Easing = new CubicEaseInOut(),
            FillMode = DefaultBottomSheetVariables.AnimationFillMode,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeStart),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, 0.0),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.EndOpacity)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeEnd),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.StartOpacity)
                    }
                }
            }
        };
    }
    
    private static Animation CreateOptimizedScrimShowAnimation()
    {
        return new Animation
        {
            Duration = DefaultBottomSheetVariables.AnimationDuration,
            Easing = new CubicEaseInOut(),
            FillMode = DefaultBottomSheetVariables.AnimationFillMode,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeStart),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.StartOpacity) }
                },
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeEnd),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity) }
                }
            }
        };
    }
    
    private static Animation CreateOptimizedScrimHideAnimation()
    {
        return new Animation
        {
            Duration = DefaultBottomSheetVariables.AnimationDuration,
            Easing = new CubicEaseInOut(),
            FillMode = DefaultBottomSheetVariables.AnimationFillMode,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeStart),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity) }
                },
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeEnd),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.StartOpacity) }
                }
            }
        };
    }

    private static void SetupScrimForAnimation(Visual view)
    {
        view.Opacity = 0.0;
        view.IsVisible = true;
    }

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsDismissableOnScrimClick && ViewModel != null)
        {
            ViewModel.HideCommand.Execute().Subscribe();
        }
    }

    private static T EnsureTransform<T>(Visual visual) where T : Transform, new()
    {
        TransformGroup? transformGroup = visual.RenderTransform as TransformGroup;

        if (transformGroup == null)
        {
            transformGroup = new TransformGroup();
            if (visual.RenderTransform is Transform existingTransform)
                transformGroup.Children.Add(existingTransform);
            visual.RenderTransform = transformGroup;
        }

        foreach (Transform? child in transformGroup.Children)
        {
            if (child is T existing) return existing;
        }

        T newTransform = new();
        transformGroup.Children.Add(newTransform);
        return newTransform;
    }
}