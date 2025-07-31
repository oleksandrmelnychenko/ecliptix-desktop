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
    private readonly TimeSpan _animationDuration =
        TimeSpan.FromMilliseconds(DefaultBottomSheetVariables.DefaultAnimationDuration);

    private double _sheetHeight;
    private Border? _sheetBorder;
    private Border? _scrimBorder;
    private ContentControl? _contentControl;

    private Animation? _showAnimation;
    private Animation? _hideAnimation;
    private Animation? _scrimShowAnimation;
    private Animation? _scrimHideAnimation;

    public static readonly StyledProperty<double> AppearVerticalOffsetProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(AppearVerticalOffset));

    public static readonly StyledProperty<double> DisappearVerticalOffsetProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(DisappearVerticalOffset));

    public static readonly StyledProperty<double> MinHeightProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(MinHeight), DefaultBottomSheetVariables.MinHeight);

    public static readonly StyledProperty<double> MaxHeightProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(MaxHeight), DefaultBottomSheetVariables.MaxHeight);

    public static readonly StyledProperty<IBrush> ScrimColorProperty =
        AvaloniaProperty.Register<BottomSheetControl, IBrush>(nameof(ScrimColor),
            DefaultBottomSheetVariables.DefaultScrimColor);

    public static readonly StyledProperty<bool> IsDismissableOnScrimClickProperty =
        AvaloniaProperty.Register<BottomSheetControl, bool>(nameof(IsDismissableOnScrimClick),
            DefaultBottomSheetVariables.DefaultIsDismissableOnScrimClick);

    public static readonly StyledProperty<IBrush> DismissableScrimColorProperty =
        AvaloniaProperty.Register<BottomSheetControl, IBrush>(nameof(DismissableScrimColor),
            DefaultBottomSheetVariables.DefaultDismissableScrimColor);

    public static readonly StyledProperty<IBrush> UnDismissableScrimColorProperty =
        AvaloniaProperty.Register<BottomSheetControl, IBrush>(nameof(UnDismissableScrimColor),
            DefaultBottomSheetVariables.DefaultUnDismissableScrimColor);

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

    public double MinHeight
    {
        get => GetValue(MinHeightProperty);
        set => SetValue(MinHeightProperty, value);
    }

    public double MaxHeight
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
            Observable.FromEventPattern<RoutedEventArgs>(this, nameof(Loaded))
                .Take(1)
                .Subscribe(_ =>
                {
                    UpdateSheetHeight();
                    CreateAnimations();
                })
                .DisposeWith(disposables);
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

        _contentControl.GetObservable(BoundsProperty)
            .Subscribe(_ =>
            {
                UpdateSheetHeight();
                CreateAnimations();
            })
            .DisposeWith(disposables);

        _contentControl.GetObservable(MarginProperty)
            .Subscribe(_ =>
            {
                UpdateSheetHeight();
                CreateAnimations();
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
                var (isDismissable, dismissableColor, unDismissableColor) = tuple;
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
                    CreateAnimations();
                    if (_showAnimation == null || _scrimShowAnimation == null)
                    {
                        IsVisible = true;
                        return;
                    }

                    IsVisible = true;
                    SetupViewForAnimation(_sheetBorder);

                    var showTasks = new List<Task> { _showAnimation.RunAsync(_sheetBorder, CancellationToken.None) };

                    if (showScrim)
                    {
                        SetupScrimForAnimation(_scrimBorder);
                        showTasks.Add(_scrimShowAnimation.RunAsync(_scrimBorder, CancellationToken.None));
                    }
                    else
                    {
                        _scrimBorder.IsVisible = false;
                    }

                    await Task.WhenAll(showTasks);
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
                    if (_hideAnimation == null || _scrimHideAnimation == null)
                    {
                        IsVisible = false;
                        return;
                    }

                    var hideTasks = new List<Task> { _hideAnimation.RunAsync(_sheetBorder, CancellationToken.None) };

                    if (_scrimBorder.IsVisible)
                    {
                        hideTasks.Add(_scrimHideAnimation.RunAsync(_scrimBorder, CancellationToken.None));
                    }

                    await Task.WhenAll(hideTasks);

                    IsVisible = false;
                    _scrimBorder.IsVisible = false;
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
            return;
        }

        double verticalMargin = _contentControl.Margin.Top + _contentControl.Margin.Bottom;
        double contentHeight = _contentControl.DesiredSize.Height + verticalMargin;
        _sheetHeight = Math.Clamp(contentHeight > 0 ? contentHeight : MinHeight, MinHeight, MaxHeight);
        _sheetBorder.Height = _sheetHeight;
    }

    private void CreateAnimations()
    {
        double hiddenPosition = _sheetHeight + DisappearVerticalOffset;

        _showAnimation = new Animation
        {
            Duration = _animationDuration,
            Easing = new CubicEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultOpacity)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, AppearVerticalOffset),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultToOpacity)
                    }
                }
            }
        };

        _hideAnimation = new Animation
        {
            Duration = _animationDuration,
            Easing = new CubicEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, AppearVerticalOffset),
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultToOpacity)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition),
                        new Setter(OpacityProperty, 0)
                    }
                }
            }
        };

        _scrimShowAnimation = new Animation
        {
            Duration = _animationDuration,
            Easing = new CubicEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0.0)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity)
                    }
                }
            }
        };

        _scrimHideAnimation = new Animation
        {
            Duration = _animationDuration,
            Easing = new CubicEaseInOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0.0),
                    Setters =
                    {
                        new Setter(OpacityProperty, DefaultBottomSheetVariables.DefaultScrimOpacity)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0.0)
                    }
                }
            }
        };
    }

    private void SetupViewForAnimation(Visual view)
    {
        view.RenderTransformOrigin = RelativePoint.TopLeft;
        TranslateTransform translateTransform = EnsureTransform<TranslateTransform>(view);
        translateTransform.Y = _sheetHeight + DisappearVerticalOffset;
        view.Opacity = DefaultBottomSheetVariables.DefaultOpacity;
        view.IsVisible = true;
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