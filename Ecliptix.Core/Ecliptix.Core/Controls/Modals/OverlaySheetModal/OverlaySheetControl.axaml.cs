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
using Avalonia.VisualTree;
using ReactiveUI;
using Serilog;
using Splat;

namespace Ecliptix.Core.Controls.Modals.OverlaySheetModal;

public sealed partial class OverlaySheetControl : ReactiveUserControl<OverlaySheetViewModel>, IDisposable
{
    public static readonly StyledProperty<IBrush> ScrimColorProperty =
        AvaloniaProperty.Register<OverlaySheetControl, IBrush>(nameof(ScrimColor),
            DefaultOverlaySheetVariables.ScrimBrush);

    public static readonly StyledProperty<bool> IsDismissableOnScrimClickProperty =
        AvaloniaProperty.Register<OverlaySheetControl, bool>(nameof(IsDismissableOnScrimClick),
            DefaultOverlaySheetVariables.DEFAULT_IS_DISMISSABLE_ON_SCRIM_CLICK);

    public static readonly StyledProperty<IBrush> DismissableScrimColorProperty =
        AvaloniaProperty.Register<OverlaySheetControl, IBrush>(nameof(DismissableScrimColor),
            DefaultOverlaySheetVariables.ScrimBrush);

    public static readonly StyledProperty<IBrush> UnDismissableScrimColorProperty =
        AvaloniaProperty.Register<OverlaySheetControl, IBrush>(nameof(UnDismissableScrimColor),
            DefaultOverlaySheetVariables.ScrimBrush);

    private bool _disposed;
    private bool _isAnimating;

    private Border? _sheetBorder;
    private Border? _scrimBorder;
    private Grid? _rootGrid;
    private ViewModelViewHost? _contentHost;

    private Animation? _showAnimation;
    private Animation? _hideAnimation;
    private Animation? _scrimShowAnimation;
    private Animation? _scrimHideAnimation;

    public OverlaySheetControl()
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

    private static void EnsureTransform<T>(Visual visual) where T : Transform, new()
    {
        ITransform? existingTransform = visual.RenderTransform;
        if (existingTransform is TransformGroup existingGroup)
        {
            if (existingGroup.Children.OfType<T>().Any())
            {
                return;
            }

            T newTransformFromGroup = new();
            existingGroup.Children.Add(newTransformFromGroup);
            return;
        }

        TransformGroup group = new();
        if (existingTransform is Transform singleTransform)
        {
            group.Children.Add(singleTransform);
        }

        T newTransform = new();
        group.Children.Add(newTransform);
        visual.RenderTransform = group;
    }

    private void InitializeDefaults()
    {
        ViewModel = Locator.Current.GetService<OverlaySheetViewModel>();
        IsDismissableOnScrimClick = DefaultOverlaySheetVariables.DEFAULT_IS_DISMISSABLE_ON_SCRIM_CLICK;

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

        if (_sheetBorder != null && _scrimBorder != null && _rootGrid != null)
        {
            EnsureTransform<ScaleTransform>(_sheetBorder);
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
                    Log.Error(e, "[OverlaySheetControl] ERROR during visibility change to {IsVisible}", isVisible);
                }
            })
            .DisposeWith(disposables);
    }

    private IInputElement? _previousFocusedElement;
    private async Task OnVisibilityChanged(bool isVisible)
    {
        if (_rootGrid is null)
        {
            return;
        }

        await WaitForAnimationToCompleteAsync();

        if (isVisible)
        {
            await ShowOverlaySheetWithFocusAsync();
        }
        else
        {
            await HideOverlaySheetWithFocusRestoreAsync();
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

        if (_isAnimating)
        {
            _isAnimating = false;
        }
    }

    private async Task ShowOverlaySheetWithFocusAsync()
    {
        SavePreviousFocusedElement();
        UpdateSheetLayout();
        await ShowOverlaySheet();
        Focus();
    }

    private void SavePreviousFocusedElement()
    {
        try
        {
            TopLevel? visualRoot = _rootGrid?.GetVisualRoot() as TopLevel;
            _previousFocusedElement = visualRoot?.FocusManager?.GetFocusedElement();
        }
        catch (Exception)
        {
            _previousFocusedElement = null;
        }
    }

    private async Task HideOverlaySheetWithFocusRestoreAsync()
    {
        await HideOverlaySheet();
        RestorePreviousFocus();
    }

    private void RestorePreviousFocus()
    {
        try
        {
            if (_previousFocusedElement is { } previous)
            {
                previous.Focus();
                _previousFocusedElement = null;
            }
            else if (Parent is Control parentControl)
            {
                parentControl.Focus();
            }
        }
        catch (Exception)
        {
            TryFocusParentControl();
        }
    }

    private void TryFocusParentControl()
    {
        if (Parent is Control fallbackParent)
        {
            fallbackParent.Focus();
        }
    }

    private void UpdateSheetLayout()
    {
        if (_contentHost == null || _sheetBorder == null)
        {
            return;
        }

        // Measure the content
        Size availableSize = new(double.PositiveInfinity, double.PositiveInfinity);
        _contentHost.Measure(availableSize);

        double contentWidth = _contentHost.DesiredSize.Width;
        double contentHeight = _contentHost.DesiredSize.Height;

        // Apply padding and border thickness
        Thickness padding = _sheetBorder.Padding;
        Thickness borderThickness = _sheetBorder.BorderThickness;

        double horizontalExtras = padding.Left + padding.Right + borderThickness.Left + borderThickness.Right;
        double verticalExtras = padding.Top + padding.Bottom + borderThickness.Top + borderThickness.Bottom;

        // Set the sheet dimensions
        _sheetBorder.Width = Math.Max(0, contentWidth + horizontalExtras);
        _sheetBorder.Height = Math.Max(0, contentHeight + verticalExtras);
    }
    
    private async Task ShowOverlaySheet()
    {
        if (_sheetBorder is null || _rootGrid is null)
        {
            return;
        }

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

    private async Task HideOverlaySheet()
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
        finally
        {
            _isAnimating = false;
        }
    }

    private void CreateAnimations()
    {
        CubicEaseOut showEasing = new(); // For a pop-in effect, start fast and decelerate
        CubicEaseIn hideEasing = new(); // For pop-out, start slow and accelerate

        _showAnimation = new Animation
        {
            Duration = OverlaySheetAnimationConstants.ShowAnimationDuration,
            Easing = showEasing,
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = {
                    new Setter(OpacityProperty, 0.0),
                    new Setter(ScaleTransform.ScaleXProperty, 0.8),
                    new Setter(ScaleTransform.ScaleYProperty, 0.8)
                } },
                new KeyFrame { Cue = new Cue(1.0), Setters = {
                    new Setter(OpacityProperty, 1.0),
                    new Setter(ScaleTransform.ScaleXProperty, 1.0),
                    new Setter(ScaleTransform.ScaleYProperty, 1.0)
                } }
            }
        };

        _hideAnimation = new Animation
        {
            Duration = OverlaySheetAnimationConstants.HideAnimationDuration,
            Easing = hideEasing,
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = {
                    new Setter(OpacityProperty, 1.0),
                    new Setter(ScaleTransform.ScaleXProperty, 1.0),
                    new Setter(ScaleTransform.ScaleYProperty, 1.0)
                } },
                new KeyFrame { Cue = new Cue(1.0), Setters = {
                    new Setter(OpacityProperty, 0.0),
                    new Setter(ScaleTransform.ScaleXProperty, 0.8),
                    new Setter(ScaleTransform.ScaleYProperty, 0.8)
                } }
            }
        };

        if (ViewModel?.ShowScrim is not true)
        {
            return;
        }

        _scrimShowAnimation = new Animation
        {
            Duration = OverlaySheetAnimationConstants.ShowAnimationDuration,
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
            Duration = OverlaySheetAnimationConstants.HideAnimationDuration,
            Easing = new CubicEaseInOut(),
            FillMode = FillMode.Both,
            Children =
            {
                new KeyFrame { Cue = new Cue(0.0), Setters = { new Setter(OpacityProperty, 0.5) } },
                new KeyFrame { Cue = new Cue(1.0), Setters = { new Setter(OpacityProperty, 0.0) } }
            }
        };
    }

    private async void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        try
        {
            if (!IsDismissableOnScrimClick || ViewModel is null || _isAnimating)
            {
                return;
            }

            ViewModel.IsVisible = false;

            await Task.Delay(OverlaySheetAnimationConstants.HideAnimationDuration);

            ViewModel.OverlaySheetDismissed();
        }
        catch
        {
            // UI catch
        }
    }

    private static readonly FrozenDictionary<Key, Func<OverlaySheetControl, bool>> DismissKeys =
        new Dictionary<Key, Func<OverlaySheetControl, bool>>
        {
            { Key.Enter, control => control.IsDismissableOnScrimClick },
            { Key.Escape, control => control.IsDismissableOnScrimClick },
            { Key.Space, control => control.IsDismissableOnScrimClick }
        }.ToFrozenDictionary();

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (DismissKeys.TryGetValue(e.Key, out Func<OverlaySheetControl, bool>? shouldDismiss) &&
            shouldDismiss(this) &&
            ViewModel is not null &&
            !_isAnimating)
        {
            ViewModel.IsVisible = false;

            await Task.Delay(OverlaySheetAnimationConstants.HideAnimationDuration);

            ViewModel.OverlaySheetDismissed();
            e.Handled = true;
        }
    }
}
