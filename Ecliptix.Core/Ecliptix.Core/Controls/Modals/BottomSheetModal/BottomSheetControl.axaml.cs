using System;
using System.Collections.Frozen;
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
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.Styling;
using Avalonia.VisualTree;
using ReactiveUI;
using Serilog;
using Splat;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public partial class BottomSheetControl : ReactiveUserControl<BottomSheetViewModel>, IDisposable
{
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

    private bool _disposed;
    private bool _isAnimating;
    private double _sheetHeight;

    private Border? _sheetBorder;
    private Border? _scrimBorder;
    private Grid? _rootGrid;
    private ContentControl? _contentControl;

    private Animation? _showAnimation;
    private Animation? _hideAnimation;
    private Animation? _scrimShowAnimation;
    private Animation? _scrimHideAnimation;

    public BottomSheetControl()
    {
        InitializeComponent();
        InitializeDefaults();
        Focusable = true;
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

    private static T EnsureTransform<T>(Visual visual) where T : Transform, new()
    {
        ITransform? existingTransform = visual.RenderTransform;
        if (existingTransform is TransformGroup existingGroup)
        {
            foreach (Transform? child in existingGroup.Children)
            {
                if (child is T specificTransform)
                {
                    return specificTransform;
                }
            }
            T newTransformFromGroup = new T();
            existingGroup.Children.Add(newTransformFromGroup);
            return newTransformFromGroup;
        }

        TransformGroup group = new TransformGroup();
        if (existingTransform is Transform singleTransform)
        {
            group.Children.Add(singleTransform);
        }

        T newTransform = new T();
        group.Children.Add(newTransform);
        visual.RenderTransform = group;
        return newTransform;
    }

    private void InitializeDefaults()
    {
        ViewModel = Locator.Current.GetService<BottomSheetViewModel>();
        IsDismissableOnScrimClick = DefaultBottomSheetVariables.DefaultIsDismissableOnScrimClick;

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
        _contentControl = this.FindControl<ContentControl>("ContentControl");

        if (_sheetBorder != null && _scrimBorder != null && _rootGrid != null)
        {
            EnsureTransform<TranslateTransform>(_sheetBorder);
            _rootGrid.IsVisible = false;
            _sheetBorder.IsVisible = false;
            _scrimBorder.IsVisible = false;
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

    private void SetupDismissableBindings(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.ViewModel)
            .WhereNotNull()
            .Take(1)
            .Subscribe(viewModel => viewModel.IsDismissableOnScrimClick = IsDismissableOnScrimClick)
            .DisposeWith(disposables);

        this.WhenAnyValue(x => x.ViewModel!.IsDismissableOnScrimClick)
            .Subscribe(isDismissable => IsDismissableOnScrimClick = isDismissable)
            .DisposeWith(disposables);
    }

    private void SetupAnimationBindings(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.ViewModel!.IsVisible)
            .Skip(1)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(async void (isVisible) =>
            {
                try
                {
                    await OnVisibilityChanged(isVisible);
                }
                catch (Exception e)
                {
                    Log.Error(e, "[BottomSheetControl] Error during visibility change to {IsVisible}", isVisible);
                }
            })
            .DisposeWith(disposables);
    }

    private IInputElement? _previousFocusedElement;
    private async Task OnVisibilityChanged(bool isVisible)
    {
        Log.Debug("[BottomSheetControl] Visibility changed: {IsVisible}", isVisible);
        Log.Debug("[BottomSheetControl] IsAnimating: {IsAnimating}", _isAnimating);

        if (_rootGrid is null)
        {
            Log.Debug("[BottomSheetControl] RootGrid is null, skipping");
            return;
        }

        if (_isAnimating)
        {
            Log.Debug("[BottomSheetControl] Animation in progress, waiting for completion...");

            DateTime timeout = DateTime.UtcNow.AddSeconds(3);
            while (_isAnimating && DateTime.UtcNow < timeout)
            {
                await Task.Delay(50);
            }

            if (_isAnimating)
            {
                Log.Warning("[BottomSheetControl] Animation timeout reached, forcing continue");
                _isAnimating = false;
            }

            Log.Debug("[BottomSheetControl] Animation wait complete, proceeding with visibility change");
        }

        if (isVisible)
        {
            Log.Information("Sheet is showing. Storing previous focus.");
            try
            {
                TopLevel? visualRoot = _rootGrid.GetVisualRoot() as TopLevel;
                _previousFocusedElement = visualRoot?.FocusManager?.GetFocusedElement();
                Log.Information("Stored previous focused element: {Element}", _previousFocusedElement?.GetType().Name ?? "null"); // Added
            }
            catch (Exception ex)
            {
                Log.Information(ex, "Exception while getting previous focus. Setting to null.");
                _previousFocusedElement = null;
            }
            UpdateSheetHeight();
            await ShowBottomSheet();
            Focus();
        }
        else
        {
            Log.Information("Sheet is hiding. Restoring previous focus.");
            await HideBottomSheet();
            try
            {
                if (_previousFocusedElement is { } previous)
                {
                    Log.Information("Restoring focus to previous element: {Element}", previous.GetType().Name);
                     previous.Focus();
                    _previousFocusedElement = null;
                }
                else if (Parent is Control parentControl)
                {
                    Log.Information("No previous element found. Falling back to focus parent: {Element}", parentControl.GetType().Name);
                    parentControl.Focus();
                }
                else
                {
                    Log.Information("No previous element and no parent to focus.");
                }
            }
            catch (Exception ex)
            {
                Log.Information(ex, "Exception while restoring focus. Attempting fallback to parent.");
                if (Parent is Control fallbackParent)
                {
                    Log.Information("Restoring focus to fallback parent: {Element}", fallbackParent.GetType().Name);
                    fallbackParent.Focus();
                }
                else
                {
                    Log.Information("Fallback focus failed. No parent found.");
                }
            }
        }
    }

    private void UpdateSheetHeight()
    {
        if (_contentControl == null || _sheetBorder == null)
        {
            _sheetHeight = MinHeight;
            return;
        }

        double sheetWidth = _sheetBorder.Width;
        if (double.IsNaN(sheetWidth) || sheetWidth <= 0)
        {
            sheetWidth = DefaultBottomSheetVariables.DefaultWidth;
        }

        Thickness padding = _sheetBorder.Padding;
        Thickness borderThickness = _sheetBorder.BorderThickness;

        double verticalExtras = padding.Top + padding.Bottom + borderThickness.Top + borderThickness.Bottom;
        double horizontalExtras = padding.Left + padding.Right + borderThickness.Left + borderThickness.Right;

        double availableWidth = sheetWidth - horizontalExtras;
        double availableHeight = MaxHeight - verticalExtras;

        Size availableSize = new Size(availableWidth, double.PositiveInfinity);
        _contentControl.Measure(availableSize);

        double contentHeight = _contentControl.DesiredSize.Height;

        if (double.IsNaN(contentHeight) || contentHeight <= 0)
        {
            contentHeight = MinHeight;
        }

        double maxContentHeight = Math.Max(MinHeight, availableHeight);

        _sheetHeight = Math.Clamp(contentHeight, MinHeight, maxContentHeight);
        _sheetBorder.Height = _sheetHeight;
    }

    private async Task ShowBottomSheet()
    {
        if (_sheetBorder is null || _rootGrid is null)
        {
            return;
        }

        UpdateSheetHeight();

        CreateAnimations();

        if (_showAnimation is null)
        {
            return;
        }

        _isAnimating = true;
        _sheetBorder.IsHitTestVisible = false;

        _rootGrid.IsVisible = true;
        _sheetBorder.IsVisible = true;

        if (ViewModel?.ShowScrim == true && _scrimBorder is not null)
        {
            _scrimBorder.IsVisible = true;
        }

        try
        {
            List<Task> showTasks = new()
            {
                _showAnimation.RunAsync(_sheetBorder, CancellationToken.None)
            };

            if (ViewModel?.ShowScrim == true && _scrimShowAnimation is not null && _scrimBorder is not null)
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

    private async Task HideBottomSheet()
    {
        if (_hideAnimation is null || _sheetBorder is null || _rootGrid is null)
        {
            return;
        }

        _isAnimating = true;
        _sheetBorder.IsHitTestVisible = false;

        try
        {
            List<Task> hideTasks = new()
            {
                _hideAnimation.RunAsync(_sheetBorder, CancellationToken.None)
            };

            if (ViewModel?.ShowScrim == true && _scrimHideAnimation is not null && _scrimBorder is not null)
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
        finally
        {
            _isAnimating = false;
        }
    }

    private void CreateAnimations()
    {
        double hiddenPosition = _sheetHeight;

        CubicEaseInOut showEasing = new();
        CubicEaseInOut hideEasing = new();

        _showAnimation = new Animation
        {
            Duration = BottomSheetAnimationConstants.ShowAnimationDuration,
            Easing = showEasing,
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(TranslateTransform.YProperty, hiddenPosition), new Setter(OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(TranslateTransform.YProperty, 0.0) } }
            }
        };

        _hideAnimation = new Animation
        {
            Duration = BottomSheetAnimationConstants.HideAnimationDuration,
            Easing = hideEasing,
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(TranslateTransform.YProperty, 0.0), new Setter(OpacityProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(TranslateTransform.YProperty, hiddenPosition), new Setter(OpacityProperty, 0.0) } }
            }
        };

        if (ViewModel?.ShowScrim == true)
        {
            _scrimShowAnimation = new Animation
            {
                Duration = BottomSheetAnimationConstants.ShowAnimationDuration,
                Easing = new CubicEaseInOut(),
                FillMode = FillMode.Both,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(OpacityProperty, 0.0) } },
                    new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(OpacityProperty, 0.5) } }
                }
            };

            _scrimHideAnimation = new Animation
            {
                Duration = BottomSheetAnimationConstants.HideAnimationDuration,
                Easing = new CubicEaseInOut(),
                FillMode = FillMode.Both,
                Children =
                {
                    new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(OpacityProperty, 0.5) } },
                    new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(OpacityProperty, 0.0) } }
                }
            };
        }
    }

    private async void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsDismissableOnScrimClick && ViewModel is not null && !_isAnimating)
        {
            ViewModel.IsVisible = false;

            await Task.Delay(BottomSheetAnimationConstants.HideAnimationDuration);

            ViewModel.BottomSheetDismissed();
        }
    }

    private static readonly FrozenDictionary<Key, Func<BottomSheetControl, bool>> DismissKeys =
        new Dictionary<Key, Func<BottomSheetControl, bool>>
        {
            { Key.Enter, control => control.IsDismissableOnScrimClick },
            { Key.Escape, control => control.IsDismissableOnScrimClick },
            { Key.Space, control => control.IsDismissableOnScrimClick }
        }.ToFrozenDictionary();

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DismissKeys.TryGetValue(e.Key, out Func<BottomSheetControl, bool>? shouldDismiss) &&
            shouldDismiss(this) &&
            ViewModel is not null &&
            !_isAnimating)
        {
            ViewModel.IsVisible = false;

            await Task.Delay(BottomSheetAnimationConstants.HideAnimationDuration);

            ViewModel.BottomSheetDismissed();
            e.Handled = true;
        }
    }

}
