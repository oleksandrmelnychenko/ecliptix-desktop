using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using ReactiveUI;
using Serilog;
using Splat;

namespace Ecliptix.Core.Controls.Modals.SideSheetModal;

public partial class SideSheetControl : ReactiveUserControl<SideSheetViewModel>, IDisposable
{
    public new static readonly StyledProperty<double> HeightProperty =
        AvaloniaProperty.Register<SideSheetControl, double>(nameof(Height), DefaultSideSheetVariables.FIXED_HEIGHT);

    public new static readonly StyledProperty<double> MaxWidthProperty =
        AvaloniaProperty.Register<SideSheetControl, double>(nameof(MaxWidth), DefaultSideSheetVariables.MAX_WIDTH);

    public static readonly StyledProperty<IBrush> ScrimColorProperty =
        AvaloniaProperty.Register<SideSheetControl, IBrush>(nameof(ScrimColor), DefaultSideSheetVariables.ScrimBrush);

    public static readonly StyledProperty<bool> IsDismissableOnScrimClickProperty =
        AvaloniaProperty.Register<SideSheetControl, bool>(nameof(IsDismissableOnScrimClick), DefaultSideSheetVariables.DEFAULT_IS_DISMISSABLE_ON_SCRIM_CLICK);

    public static readonly StyledProperty<IBrush> DismissableScrimColorProperty =
        AvaloniaProperty.Register<SideSheetControl, IBrush>(nameof(DismissableScrimColor), DefaultSideSheetVariables.ScrimBrush);

    public static readonly StyledProperty<IBrush> UnDismissableScrimColorProperty =
        AvaloniaProperty.Register<SideSheetControl, IBrush>(nameof(UnDismissableScrimColor), DefaultSideSheetVariables.ScrimBrush);

    private bool _disposed;
    private bool _isAnimating;
    private double _sheetWidth;

    private Border? _sheetBorder;
    private Border? _scrimBorder;
    private Grid? _rootGrid;
    private ViewModelViewHost? _contentHost;
    private Canvas? _measureContainer;

    private Animation? _showAnimation;
    private Animation? _hideAnimation;
    private Animation? _scrimShowAnimation;
    private Animation? _scrimHideAnimation;

    public SideSheetControl()
    {
        InitializeComponent();
        InitializeDefaults();
        Focusable = true;
    }

    public new double Height
    {
        get => GetValue(HeightProperty);
        set => SetValue(HeightProperty, value);
    }

    public new double MaxWidth
    {
        get => GetValue(MaxWidthProperty);
        set => SetValue(MaxWidthProperty, value);
    }

    public IBrush ScrimColor
    {
        get => GetValue(ScrimColorProperty);
        set => SetValue(ScrimColorProperty, value);
    }

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

    public bool IsDismissableOnScrimClick
    {
        get => GetValue(IsDismissableOnScrimClickProperty);
        set => SetValue(IsDismissableOnScrimClickProperty, value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (ViewModel is IActivatableViewModel activatable)
        {
            activatable.Activator.Deactivate();
        }

        if (ViewModel is IDisposable disposableViewModel)
        {
            disposableViewModel.Dispose();
        }

        _disposed = true;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        KeyDown += OnKeyDown;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        KeyDown -= OnKeyDown;
        Dispose();
    }

    private static void EnsureTransform<T>(Visual visual) where T : Transform, new()
    {
        ITransform? existingTransform = visual.RenderTransform;
        if (existingTransform is TransformGroup existingGroup)
        {
            if (existingGroup.Children.OfType<T>().Any())
            {
                return;
            }

            existingGroup.Children.Add(new T());
            return;
        }

        TransformGroup group = new();
        if (existingTransform is Transform singleTransform)
        {
            group.Children.Add(singleTransform);
        }

        group.Children.Add(new T());
        visual.RenderTransform = group;
    }

    private void InitializeDefaults()
    {
        ViewModel = Locator.Current.GetService<SideSheetViewModel>();
        IsDismissableOnScrimClick = DefaultSideSheetVariables.DEFAULT_IS_DISMISSABLE_ON_SCRIM_CLICK;
        if (ViewModel is IActivatableViewModel activatableViewModel)
        {
            activatableViewModel.Activator.Activate();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        InitializeControls();
        SetupReactiveBindings();
    }

    private void InitializeControls()
    {
        _rootGrid = this.FindControl<Grid>("RootGrid");
        _sheetBorder = this.FindControl<Border>("SheetBorder");
        _scrimBorder = this.FindControl<Border>("ScrimBorder");
        _contentHost = this.FindControl<ViewModelViewHost>("ContentHost");
        _measureContainer = this.FindControl<Canvas>("HiddenMeasureContainer");

        if (_sheetBorder != null && _scrimBorder != null && _rootGrid != null)
        {
            EnsureTransform<TranslateTransform>(_sheetBorder);
            _rootGrid.IsVisible = false;
            _sheetBorder.IsVisible = false;
            _scrimBorder.IsVisible = false;


            _sheetBorder.Height = Height;
        }
    }

    private void SetupReactiveBindings()
    {
        this.WhenActivated(disposables =>
        {
            SetupDismissableBindings(disposables);
            SetupScrimColorBinding(disposables);
            SetupAnimationBindings(disposables);
        });
    }

    private void SetupScrimColorBinding(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.IsDismissableOnScrimClick, x => x.DismissableScrimColor, x => x.UnDismissableScrimColor)
           .Subscribe(t => ScrimColor = t.Item1 ? t.Item2 : t.Item3)
           .DisposeWith(disposables);
    }

    private void SetupDismissableBindings(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.ViewModel).WhereNotNull().Take(1)
            .Subscribe(vm => vm.IsDismissableOnScrimClick = IsDismissableOnScrimClick).DisposeWith(disposables);
        this.WhenAnyValue(x => x.ViewModel!.IsDismissableOnScrimClick)
            .Subscribe(v => IsDismissableOnScrimClick = v).DisposeWith(disposables);
    }

    private void SetupAnimationBindings(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.ViewModel!.IsVisible).Skip(1).ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async isVisible => { try { await OnVisibilityChanged(isVisible); } catch (Exception e) { Log.Error(e, "Error"); } })
            .DisposeWith(disposables);
    }

    private async Task OnVisibilityChanged(bool isVisible)
    {
        if (_rootGrid is null)
        {
            return;
        }

        await WaitForAnimationToCompleteAsync();
        if (isVisible)
        {
            await ShowSideSheetWithFocusAsync();
        }
        else
        {
            await HideSideSheetWithFocusRestoreAsync();
        }
    }

    private async Task WaitForAnimationToCompleteAsync()
    {
        if (!_isAnimating)
        {
            return;
        }

        DateTime timeout = DateTime.UtcNow.AddSeconds(3);
        while (_isAnimating && DateTime.UtcNow < timeout)
        {
            await Task.Delay(50);
        }

        _isAnimating = false;
    }

    private async Task ShowSideSheetWithFocusAsync()
    {
        UpdateSheetLayout();
        await ShowSideSheet();
        Focus();
    }

    private async Task HideSideSheetWithFocusRestoreAsync() => await HideSideSheet();

    private void UpdateSheetLayout()
    {
        if (ViewModel?.Content == null || _sheetBorder == null || _measureContainer == null)
        {
            _sheetWidth = DefaultSideSheetVariables.DEFAULT_WIDTH;
            return;
        }

        Control? viewToMeasure = CreateViewForViewModel(ViewModel.Content);
        if (viewToMeasure == null)
        {
            return;
        }

        _measureContainer.Children.Add(viewToMeasure);

        Thickness padding = _sheetBorder.Padding;
        Thickness borderThickness = _sheetBorder.BorderThickness;
        double horizontalExtras = padding.Left + padding.Right + borderThickness.Left + borderThickness.Right;

        _ = padding.Top + padding.Bottom + borderThickness.Top + borderThickness.Bottom;

        double availableWidth = MaxWidth - horizontalExtras;

        double availableHeight = _rootGrid != null ? _rootGrid.Bounds.Height : Height;
        if (availableHeight == 0)
        {
            availableHeight = double.PositiveInfinity;
        }

        viewToMeasure.Measure(new Size(availableWidth, availableHeight));
        double contentWidth = viewToMeasure.DesiredSize.Width;

        _measureContainer.Children.Remove(viewToMeasure);

        if (double.IsNaN(contentWidth) || contentWidth <= 0)
        {
            contentWidth = DefaultSideSheetVariables.DEFAULT_WIDTH;
        }

        _sheetWidth = Math.Clamp(contentWidth + horizontalExtras, 50, MaxWidth);
        _sheetBorder.Width = _sheetWidth;
    }

    private Control? CreateViewForViewModel(object viewModel)
    {
        IViewLocator viewLocator = ViewLocator.Current;
        IViewFor? view = viewLocator.ResolveView(viewModel);
        if (view is Control control)
        {
            control.DataContext = viewModel;
            view.ViewModel = viewModel;
            return control;
        }
        return null;
    }

    private async Task ShowSideSheet()
    {
        if (_sheetBorder is null || _rootGrid is null)
        {
            return;
        }

        UpdateSheetLayout();
        CreateAnimations();

        if (_showAnimation is null)
        {
            return;
        }

        _isAnimating = true;
        _sheetBorder.IsHitTestVisible = false;

        _rootGrid.IsVisible = true;
        _sheetBorder.IsVisible = true;

        if (ViewModel?.ShowScrim is true && _scrimBorder is not null)
        {
            _scrimBorder.IsVisible = true;
        }

        try
        {
            List<Task> showTasks = [_showAnimation.RunAsync(_sheetBorder, CancellationToken.None)];
            if (ViewModel?.ShowScrim is true && _scrimShowAnimation is not null && _scrimBorder is not null)
            {
                showTasks.Add(_scrimShowAnimation.RunAsync(_scrimBorder, CancellationToken.None));
            }

            await Task.WhenAll(showTasks);
        }
        finally
        {
            _isAnimating = false;
            _sheetBorder.IsHitTestVisible = true;
        }
    }

    private async Task HideSideSheet()
    {
        if (_hideAnimation is null || _sheetBorder is null || _rootGrid is null)
        {
            return;
        }

        _isAnimating = true;
        _sheetBorder.IsHitTestVisible = false;

        try
        {
            List<Task> hideTasks = [_hideAnimation.RunAsync(_sheetBorder, CancellationToken.None)];
            if (ViewModel?.ShowScrim is true && _scrimHideAnimation is not null && _scrimBorder is not null)
            {
                hideTasks.Add(_scrimHideAnimation.RunAsync(_scrimBorder, CancellationToken.None));
            }

            await Task.WhenAll(hideTasks);

            _sheetBorder.IsVisible = false;
            if (_scrimBorder is not null)
            {
                _scrimBorder.IsVisible = false;
            }

            _rootGrid.IsVisible = false;
        }
        finally { _isAnimating = false; }
    }

    private void CreateAnimations()
    {
        double hiddenPosition = _sheetWidth + 20;

        CubicEaseInOut easing = new();

        _showAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = easing,
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(TranslateTransform.XProperty, hiddenPosition), new Setter(OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(TranslateTransform.XProperty, 0.0) } }
            }
        };

        _hideAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = easing,
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(TranslateTransform.XProperty, 0.0), new Setter(OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(TranslateTransform.XProperty, hiddenPosition), new Setter(OpacityProperty, 0.0) } }
            }
        };

        if (ViewModel?.ShowScrim is not true)
        {
            return;
        }

        _scrimShowAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = easing,
            FillMode = FillMode.Both,
            Children = { new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(OpacityProperty, 0.0) } }, new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(OpacityProperty, 0.5) } } }
        };
        _scrimHideAnimation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(400),
            Easing = easing,
            FillMode = FillMode.Both,
            Children = { new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(OpacityProperty, 0.5) } }, new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(OpacityProperty, 0.0) } } }
        };
    }

    private async void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!IsDismissableOnScrimClick || ViewModel is null || _isAnimating)
        {
            return;
        }

        ViewModel.IsVisible = false;
        await Task.Delay(400);
        ViewModel.SideSheetDismissed();
    }

    private static readonly FrozenDictionary<Key, Func<SideSheetControl, bool>> DismissKeys =
        new Dictionary<Key, Func<SideSheetControl, bool>>
        {
            { Key.Enter, c => c.IsDismissableOnScrimClick },
            { Key.Escape, c => c.IsDismissableOnScrimClick },
            { Key.Space, c => c.IsDismissableOnScrimClick }
        }.ToFrozenDictionary();

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!DismissKeys.TryGetValue(e.Key, out Func<SideSheetControl, bool>? shouldDismiss) ||
            !shouldDismiss(this) || ViewModel is null || _isAnimating)
        {
            return;
        }

        ViewModel.IsVisible = false;
        await Task.Delay(400);
        ViewModel.SideSheetDismissed();
        e.Handled = true;
    }
}

