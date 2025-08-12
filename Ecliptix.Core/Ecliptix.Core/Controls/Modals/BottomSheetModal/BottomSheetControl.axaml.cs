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
        
        // Ensure default value is set
        IsDismissableOnScrimClick = DefaultBottomSheetVariables.DefaultIsDismissableOnScrimClick;
        Log.Debug($"BottomSheetControl initialized - IsDismissableOnScrimClick: {IsDismissableOnScrimClick}");
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
        
        // Ensure all elements are initially hidden
        if (_sheetBorder != null) _sheetBorder.IsVisible = false;
        if (_scrimBorder != null) _scrimBorder.IsVisible = false;
        if (_contentControl != null) _contentControl.IsVisible = false;
        
        Log.Debug("BottomSheetControl elements initialized and hidden");
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
        _sheetBorder!.IsVisible = true;
        _scrimBorder!.IsVisible = true;
        if (_contentControl != null)
        {
            _contentControl.IsVisible = true;
        }
        
        SetupViewForAnimation(_sheetBorder!);
        
        // Defer content loading
        _pendingContent = _contentControl?.Content;
        if (_contentControl != null) _contentControl.Content = null;
        
        List<Task> showTasks = [_showAnimation.RunAsync(_sheetBorder!, CancellationToken.None)];

        // Always show scrim for hit testing (even if transparent) to allow dismissal clicks
        SetupScrimForAnimation(_scrimBorder!);
        
        if (showScrim)
        {
            // Show scrim with animation
            showTasks.Add(_scrimShowAnimation.RunAsync(_scrimBorder!, CancellationToken.None));
        }
        else
        {
            // Keep scrim transparent but visible for hit testing
            _scrimBorder!.Opacity = 0.0;
            _scrimBorder!.IsVisible = true;
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
            DisposeCurrentContent();
            IsVisible = false;
            return;
        }

        if (_isAnimating)
        {
            Log.Debug("Animation in progress but forcing hide animation");
            _isAnimating = false;
        }

        Log.Debug("Starting hide animation");
        
        _scrimBorder!.ZIndex = 0;
        _sheetBorder!.ZIndex = 1;
        
        List<Task> hideTasks = [_hideAnimation.RunAsync(_sheetBorder!, CancellationToken.None)];

        if (_scrimBorder!.IsVisible)
        {
            Log.Debug("Starting scrim hide animation");
            hideTasks.Add(_scrimHideAnimation.RunAsync(_scrimBorder, CancellationToken.None));
        }

        await Task.WhenAll(hideTasks);
        
        Log.Debug("Hide animations completed, adding small delay before cleanup");
        
        await Task.Delay(TimeSpan.FromMilliseconds(16), CancellationToken.None);
        
        DisposeCurrentContent();
        
        if (ViewModel != null)
        {
            ViewModel.Content = null;
            ViewModel.IsVisible = false;
            Log.Debug("ViewModel state reset - Content cleared, IsVisible = false");
        }
        
        IsVisible = false;
        _scrimBorder.IsVisible = false;
        _sheetBorder!.IsVisible = false;
        if (_contentControl != null)
        {
            _contentControl.IsVisible = false;
        }
        
        _scrimBorder.Opacity = 0;
        _sheetBorder.Opacity = 0;
        
        _isAnimating = false;
        _contentLoaded = false;
        
        Log.Debug("Hide animation cleanup complete - ViewModel and UI elements hidden");
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
    
    private void DisposeCurrentContent()
    {
        if (_contentControl?.Content is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
                Log.Debug("Successfully disposed current bottom sheet content");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to dispose bottom sheet content");
            }
        }
        
        if (_contentControl != null)
        {
            _contentControl.Content = null;
        }
        
        _pendingContent = null;
        _contentLoaded = false;
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
                    Cue = new Cue(0.08), // Ultra-early emergence
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.95),
                        new Setter(OpacityProperty, 0.03), // Barely visible start
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.002),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.002)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.15), // Very early gentle emergence
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.88),
                        new Setter(OpacityProperty, 0.08), // Ultra-gentle start
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.004),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.004)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.22), // Early progression
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.78),
                        new Setter(OpacityProperty, 0.15), // Gentle build
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.006),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.006)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.3), // Gradual build-up
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.65),
                        new Setter(OpacityProperty, 0.25), // Slow progressive fade
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.008),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.008)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.38), // Continuing progression
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.55),
                        new Setter(OpacityProperty, 0.35), // Building visibility
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.011),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.011)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.45), // Mid-fade continuation
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.45),
                        new Setter(OpacityProperty, 0.46), // Approaching halfway
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.014),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.014)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.52), // Mid-point refinement
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.35),
                        new Setter(OpacityProperty, 0.58), // Past halfway
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.017),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.017)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.58), // Building momentum
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.25),
                        new Setter(OpacityProperty, 0.68), // Strong visibility
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.019),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.019)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeMid), // 0.65
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, DefaultBottomSheetVariables.VerticalOvershoot * 1.5), // More overshoot
                        new Setter(OpacityProperty, 0.78), // Building to overshoot
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleOvershoot),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleOvershoot)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.72), // Post-overshoot settle
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, DefaultBottomSheetVariables.VerticalOvershoot * 0.6),
                        new Setter(OpacityProperty, 0.86), // Nearly there
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd + 0.004),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd + 0.004)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.8), // Pre-final gentle approach
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, -0.8), // Subtle pre-settle
                        new Setter(OpacityProperty, 0.92), // Almost complete
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd + 0.002),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd + 0.002)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.88), // Final approach
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, -0.3), // Micro-settle
                        new Setter(OpacityProperty, 0.97), // Nearly perfect
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd + 0.001),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd + 0.001)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.95), // Ultra-fine final approach
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, -0.1), // Micro adjustment
                        new Setter(OpacityProperty, 0.99), // Almost there
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd + 0.0005),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd + 0.0005)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeEnd),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, 0.0),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.EndOpacity), // Final perfection
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
            Easing = new SpringEasing(), // Use same easing as show for consistency
            FillMode = DefaultBottomSheetVariables.AnimationFillMode,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeStart),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, 0.0),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.EndOpacity), // Start fully visible
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.05), // Ultra-early fade start
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.02),
                        new Setter(OpacityProperty, 0.97), // Barely noticeable start
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd - 0.001),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd - 0.001)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.12), // Very early fade
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.06),
                        new Setter(OpacityProperty, 0.92), // Gentle beginning
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd - 0.002),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd - 0.002)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.2), // Early gentle fade
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.12),
                        new Setter(OpacityProperty, 0.86), // Progressive fade start
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd - 0.004),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd - 0.004)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.28), // Building fade
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.2),
                        new Setter(OpacityProperty, 0.78), // Continuing fade
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd - 0.007),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd - 0.007)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.35), // Progressive fade
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.28),
                        new Setter(OpacityProperty, 0.68), // More transparent
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd - 0.01),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd - 0.01)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.42), // Approaching mid-point
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.38),
                        new Setter(OpacityProperty, 0.58), // Past halfway visibility
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd - 0.013),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd - 0.013)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5), // Mid-point fade
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.48),
                        new Setter(OpacityProperty, 0.48), // True halfway
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd - 0.016),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd - 0.016)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.58), // Post mid-point
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.58),
                        new Setter(OpacityProperty, 0.38), // More fade
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleEnd - 0.019),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleEnd - 0.019)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.65), // Accelerating fade
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.68),
                        new Setter(OpacityProperty, 0.28), // Strong transparency
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.012),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.012)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.72), // Strong fade
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.78),
                        new Setter(OpacityProperty, 0.18), // Very transparent
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.008),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.008)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.8), // Near completion
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.88),
                        new Setter(OpacityProperty, 0.1), // Almost invisible
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.004),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.004)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.88), // Final approach
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.95),
                        new Setter(OpacityProperty, 0.04), // Nearly gone
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart + 0.001),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart + 0.001)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.95), // Ultra-final
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition * 0.98),
                        new Setter(OpacityProperty, 0.01), // Barely there
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart - 0.005),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart - 0.005)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(DefaultBottomSheetVariables.KeyframeEnd),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.StartOpacity), // Fully transparent
                        new Setter(ScaleTransform.ScaleXProperty, DefaultBottomSheetVariables.ScaleStart - 0.02),
                        new Setter(ScaleTransform.ScaleYProperty, DefaultBottomSheetVariables.ScaleStart - 0.02)
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
            Easing = new SpringEasing(), // Match main animation easing
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
                    Cue = new Cue(0.08),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.05) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.15),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.12) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.22),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.22) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.3),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.35) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.38),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.48) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.45),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.6) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.52),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.72) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.58),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.82) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.65),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.9) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.72),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.95) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.8),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.98) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.88),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.995) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.95),
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.999) }
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
            Easing = new SpringEasing(), // Match show animation easing
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
                    Cue = new Cue(0.2), // Keep full opacity longer to prevent background bleed
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.3), // Start fade later
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.92) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.4), // Progressive fade
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.8) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5), // Mid-point fade
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.65) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.6), // Continuing fade
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.48) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.7), // Accelerated fade
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.3) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.8), // Strong fade
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.15) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.88), // Nearly gone
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.05) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.94), // Final approach
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.015) }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.98), // Ultra-final hold
                    Setters = { new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity * 0.005) }
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
        Log.Debug($"SetupScrimForAnimation: Scrim set to visible with opacity 0.0, IsHitTestVisible: {((Border)view).IsHitTestVisible}");
    }

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Log.Debug("OnScrimPointerPressed called - IsDismissableOnScrimClick: {IsDismissableOnScrimClick}, ViewModel: {Unknown}", IsDismissableOnScrimClick, ViewModel != null);
        
        if (IsDismissableOnScrimClick && ViewModel != null)
        {
            Log.Debug("Dismissing bottom sheet via scrim click");
            
            _isAnimating = false;
            
            ViewModel.Content = null;
            ViewModel.IsVisible = false;
            
            Log.Debug("Forced ViewModel state to close - Content=null, IsVisible=false");
            
            ViewModel.HideCommand.Execute().Subscribe();
        }
        else
        {
            Log.Debug("Scrim click ignored - dismissal disabled or no ViewModel");
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