using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
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
        
        IsDismissableOnScrimClick = DefaultBottomSheetVariables.DefaultIsDismissableOnScrimClick;
        Log.Debug("BottomSheetControl initialized - IsDismissableOnScrimClick: {IsDismissableOnScrimClick}", IsDismissableOnScrimClick);
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
                (bool isVisible, bool showScrim) = states.Current;
                (bool wasVisible, bool wasScrimShowing) = states.Previous;

                if (_sheetBorder == null || _scrimBorder == null)
                {
                    IsVisible = isVisible;
                    return;
                }
                
                switch (isVisible)
                {
                    case true when !wasVisible:
                    {
                        if (_isAnimating) return;
                    
                        _isAnimating = true;
                        await ShowWithAnimation(showScrim);
                        _isAnimating = false;
                        break;
                    }
                    case true when showScrim && !wasScrimShowing:
                    {
                        _scrimBorder.Opacity = DefaultBottomSheetVariables.DefaultScrimOpacity;
                        break;
                    }
                    case false when wasVisible:
                    {
                        if (_isAnimating) return;
                    
                        _isAnimating = true;
                        await HideWithIOSStyleAnimation();
                        _isAnimating = false;
                        break;
                    }
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

        Size availableSize = new(_contentControl.Bounds.Width > 0 ? _contentControl.Bounds.Width : double.PositiveInfinity, double.PositiveInfinity);
        _contentControl.Measure(availableSize);

        if (!_contentControl.IsMeasureValid)
        {
            _contentControl.InvalidateMeasure();
            _contentControl.Measure(availableSize);
        }

        double verticalMargin = _contentControl.Margin.Top + _contentControl.Margin.Bottom;
        double contentHeight = _contentControl.DesiredSize.Height + verticalMargin;

        if (double.IsNaN(contentHeight) || contentHeight <= 0)
        {
            contentHeight = MinHeight;
        }

        _sheetHeight = Math.Clamp(contentHeight, MinHeight, MaxHeight);
        _sheetBorder.Height = _sheetHeight;

        Log.Debug("UpdateSheetHeight: ContentHeight={ContentHeight}, SheetHeight={SheetHeight}", contentHeight, _sheetHeight);
    }

    private void PrepareForAnimation()
    {
        if (_sheetBorder == null) return;
        
        _sheetBorder.RenderTransformOrigin = RelativePoint.Center;
        if (_sheetBorder.RenderTransform == null)
        {
            _sheetBorder.RenderTransform = new TransformGroup
            {
                Children = { new TranslateTransform(), new ScaleTransform() }
            };
        }
    }

    private async Task ShowWithAnimation(bool showScrim)
    {
        if (_sheetBorder == null || _scrimBorder == null) 
        {
            IsVisible = true;
            return;
        }

        IsVisible = true;
        _sheetBorder.IsVisible = true;
        _scrimBorder.IsVisible = true;
        if (_contentControl != null)
        {
            _contentControl.IsVisible = true;
        }
        
        PrepareForAnimation();
        
        // Set initial hidden state
        double hiddenY = _sheetHeight + DisappearVerticalOffset;
        _sheetBorder.RenderTransform = new TransformGroup
        {
            Children = {
                new TranslateTransform { Y = hiddenY },
                new ScaleTransform { ScaleX = 0.95, ScaleY = 0.95 }
            }
        };
        _sheetBorder.Opacity = 0.0;
        _scrimBorder.Opacity = 0.0;
        
        _pendingContent = _contentControl?.Content;
        if (_contentControl != null) _contentControl.Content = null;
        
        // Trigger animations by changing to final state
        await Task.Delay(50); // Small delay to ensure initial state is applied
        
        _sheetBorder.RenderTransform = new TransformGroup
        {
            Children = {
                new TranslateTransform { Y = 0 },
                new ScaleTransform { ScaleX = 1.0, ScaleY = 1.0 }
            }
        };
        _sheetBorder.Opacity = 1.0;
        
        if (showScrim)
        {
            _scrimBorder.Opacity = DefaultBottomSheetVariables.DefaultScrimOpacity;
        }
        
        Task contentTask = LoadContentAfterDelay();
        await Task.WhenAll(Task.Delay(350), contentTask); // Wait for animation duration
    }
    
    private async Task HideWithIOSStyleAnimation()
    {
        if (_sheetBorder == null || _scrimBorder == null)
        {
            DisposeCurrentContent();
            IsVisible = false;
            return;
        }

        Log.Debug("Starting hide animation");
        
        DisposeCurrentContent();
        
        _scrimBorder.ZIndex = 0;
        _sheetBorder.ZIndex = 1;
        
        // Animate to hidden state
        double hiddenY = _sheetHeight + DisappearVerticalOffset;
        _sheetBorder.RenderTransform = new TransformGroup
        {
            Children = {
                new TranslateTransform { Y = hiddenY },
                new ScaleTransform { ScaleX = 0.95, ScaleY = 0.95 }
            }
        };
        _sheetBorder.Opacity = 0.0;
        _scrimBorder.Opacity = 0.0;
        
        await Task.Delay(350); // Wait for animation duration
        
        IsVisible = false;
        _scrimBorder.IsVisible = false;
        _sheetBorder.IsVisible = false;
        if (_contentControl != null)
        {
            _contentControl.IsVisible = false;
        }
        
        _contentLoaded = false;
        
        Log.Debug("Hide animation complete");
    }
    
    private async Task LoadContentAfterDelay()
    {
        if (_contentLoaded || _pendingContent == null || _contentControl == null) return;
        
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

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsDismissableOnScrimClick && ViewModel != null && !_isAnimating)
        {
            Log.Debug("Dismissing bottom sheet via scrim click");
            ViewModel.IsVisible = false;
        }
    }

}