using System;
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

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

/// <summary>
/// A control for displaying a bottom sheet modal with animated show/hide behavior.
/// </summary>
public partial class BottomSheetControl : ReactiveUserControl<BottomSheetViewModel>
{
    // Private fields
    private readonly TimeSpan _animationDuration = TimeSpan.FromMilliseconds(DefaultBottomSheetVariables.DefaultAnimationDuration);
    private double _sheetHeight;
    private Border? _sheetBorder;
    private Border? _scrimBorder;
    private ItemsControl? _contentItems;

    // Animation fields
    private Animation? _showAnimation;
    private Animation? _hideAnimation;
    private Animation? _scrimShowAnimation;
    private Animation? _scrimHideAnimation;

    /// <summary>
    /// Defines the <see cref="AppearVerticalOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> AppearVerticalOffsetProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(AppearVerticalOffset),
            DefaultBottomSheetVariables.DefaultAppearVerticalOffset);

    /// <summary>
    /// Defines the <see cref="DisappearVerticalOffset"/> property.
    /// </summary>
    public static readonly StyledProperty<double> DisappearVerticalOffsetProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(DisappearVerticalOffset),
            DefaultBottomSheetVariables.DefaultDisappearVerticalOffset);

    /// <summary>
    /// Defines the <see cref="MinHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MinHeightProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(MinHeight), DefaultBottomSheetVariables.MinHeight);

    /// <summary>
    /// Defines the <see cref="MaxHeight"/> property.
    /// </summary>
    public static readonly StyledProperty<double> MaxHeightProperty =
        AvaloniaProperty.Register<BottomSheetControl, double>(nameof(MaxHeight), DefaultBottomSheetVariables.MaxHeight);

    /// <summary>
    /// Defines the <see cref="ScrimColor"/> property.
    /// </summary>
    public static readonly StyledProperty<IBrush> ScrimColorProperty =
        AvaloniaProperty.Register<BottomSheetControl, IBrush>(nameof(ScrimColor),
            DefaultBottomSheetVariables.DefaultScrimColor);

    /// <summary>
    /// Defines the <see cref="IsDismissableOnScrimClick"/> property.
    /// </summary>
    public static readonly StyledProperty<bool> IsDismissableOnScrimClickProperty =
        AvaloniaProperty.Register<BottomSheetControl, bool>(nameof(IsDismissableOnScrimClick),
            DefaultBottomSheetVariables.DefaultIsDismissableOnScrimClick);

    /// <summary>
    /// Gets or sets the vertical offset for the bottom sheet's appearance animation.
    /// </summary>
    public double AppearVerticalOffset
    {
        get => GetValue(AppearVerticalOffsetProperty);
        set => SetValue(AppearVerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical offset for the bottom sheet's disappearance animation.
    /// </summary>
    public double DisappearVerticalOffset
    {
        get => GetValue(DisappearVerticalOffsetProperty);
        set => SetValue(DisappearVerticalOffsetProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum height of the bottom sheet.
    /// </summary>
    public double MinHeight
    {
        get => GetValue(MinHeightProperty);
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the bottom sheet.
    /// </summary>
    public double MaxHeight
    {
        get => GetValue(MaxHeightProperty);
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the color of the scrim (background overlay).
    /// </summary>
    public IBrush ScrimColor
    {
        get => GetValue(ScrimColorProperty);
        set => SetValue(ScrimColorProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the bottom sheet can be dismissed by clicking the scrim.
    /// </summary>
    public bool IsDismissableOnScrimClick
    {
        get => GetValue(IsDismissableOnScrimClickProperty);
        set => SetValue(IsDismissableOnScrimClickProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BottomSheetControl"/> class.
    /// </summary>
    public BottomSheetControl()
    {
        InitializeComponent();
        ViewModel = Locator.Current.GetService<BottomSheetViewModel>() ?? throw new InvalidOperationException("BottomSheetViewModel not found in service locator.");
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
        });
    }

    /// <summary>
    /// Initializes control references and sets up initial state.
    /// </summary>
    private void InitializeControls()
    {
        _sheetBorder = this.FindControl<Border>("SheetBorder");
        _scrimBorder = this.FindControl<Border>("ScrimBorder");
        _contentItems = this.FindControl<ItemsControl>("ContentItems");

        if (_sheetBorder == null) Log.Warning("SheetBorder control not found.");
        if (_scrimBorder == null) Log.Warning("ScrimBorder control not found.");
        if (_contentItems == null) Log.Warning("ContentItems control not found.");

        UpdateSheetHeight();
        CreateAnimations();
    }

    /// <summary>
    /// Sets up observables for content changes and updates sheet height.
    /// </summary>
    /// <param name="disposables">The disposables collection for cleanup.</param>
    private void SetupContentObservables(CompositeDisposable disposables)
    {
        if (_contentItems == null)
        {
            Log.Warning("SetupContentObservables: ContentItems is null.");
            return;
        }

        _contentItems.GetObservable(BoundsProperty)
            .Subscribe(_ =>
            {
                UpdateSheetHeight();
                CreateAnimations();
                Log.Information($"Content bounds changed: SheetHeight={_sheetHeight}");
            })
            .DisposeWith(disposables);

        _contentItems.GetObservable(MarginProperty)
            .Subscribe(_ =>
            {
                UpdateSheetHeight();
                CreateAnimations();
                Log.Information($"Content margin changed: SheetHeight={_sheetHeight}");
            })
            .DisposeWith(disposables);
    }

    /// <summary>
    /// Sets up observables for visibility changes and animations.
    /// </summary>
    /// <param name="disposables">The disposables collection for cleanup.</param>
    private void SetupVisibilityObservable(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.ViewModel!.IsVisible)
            .Subscribe(async isVisible =>
            {
                if (_sheetBorder == null || _scrimBorder == null)
                {
                    Log.Warning("SetupVisibilityObservable: SheetBorder or ScrimBorder is null.");
                    IsVisible = isVisible;
                    return;
                }

                if (_showAnimation == null || _hideAnimation == null || _scrimShowAnimation == null || _scrimHideAnimation == null)
                {
                    Log.Warning("SetupVisibilityObservable: Animations are not initialized.");
                    IsVisible = isVisible;
                    return;
                }

                SetupViewForAnimation(_sheetBorder);
                SetupScrimForAnimation(_scrimBorder);

                if (isVisible)
                {
                    IsVisible = true;
                    await Task.WhenAll(
                        _showAnimation.RunAsync(_sheetBorder, CancellationToken.None),
                        _scrimShowAnimation.RunAsync(_scrimBorder, CancellationToken.None)
                    );
                    Log.Information("Show animations completed.");
                }
                else
                {
                    await Task.WhenAll(
                        _hideAnimation.RunAsync(_sheetBorder, CancellationToken.None),
                        _scrimHideAnimation.RunAsync(_scrimBorder, CancellationToken.None)
                    );
                    await Task.Delay(_animationDuration);
                    IsVisible = false;
                    Log.Information("Hide animations completed.");
                }
            })
            .DisposeWith(disposables);
    }

    /// <summary>
    /// Sets up observables for dismissable behavior.
    /// </summary>
    /// <param name="disposables">The disposables collection for cleanup.</param>
    private void SetupDismissableCommand(CompositeDisposable disposables)
    {
        this.WhenAnyValue(x => x.ViewModel)
            .Where(vm => vm != null)
            .Take(1)
            .Subscribe(viewModel => { viewModel!.IsDismissableOnScrimClick = IsDismissableOnScrimClick; })
            .DisposeWith(disposables);

        this.WhenAnyValue(x => x.ViewModel!.IsDismissableOnScrimClick)
            .Subscribe(isDismissable =>
            {
                IsDismissableOnScrimClick = isDismissable;
                Log.Information($"IsDismissableOnScrimClick updated: {isDismissable}");
            })
            .DisposeWith(disposables);
    }

    /// <summary>
    /// Updates the sheet height based on content size.
    /// </summary>
    private void UpdateSheetHeight()
    {
        if (_contentItems == null || _sheetBorder == null)
        {
            Log.Warning("UpdateSheetHeight: ContentItems or SheetBorder is null.");
            return;
        }

        double verticalMargin = _contentItems.Margin.Top + _contentItems.Margin.Bottom;
        double contentHeight = _contentItems.DesiredSize.Height + verticalMargin;
        _sheetHeight = Math.Clamp(contentHeight, MinHeight, MaxHeight);
        _sheetBorder.Height = _sheetHeight;
    }

    /// <summary>
    /// Creates animations for showing and hiding the bottom sheet and scrim.
    /// </summary>
    private void CreateAnimations()
    {
        if (_sheetHeight <= 0)
        {
            Log.Warning("Cannot create animations: SheetHeight is not set.");
            _showAnimation = null;
            _hideAnimation = null;
            _scrimShowAnimation = null;
            _scrimHideAnimation = null;
            return;
        }

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
                        new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultOpacity)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, AppearVerticalOffset),
                        new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultToOpacity)
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
                        new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultToOpacity)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(TranslateTransform.YProperty, hiddenPosition),
                        new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultOpacity)
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
                        new Setter(Visual.OpacityProperty, 0.0)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultOpacity)
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
                        new Setter(Visual.OpacityProperty, DefaultBottomSheetVariables.DefaultOpacity)
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1.0),
                    Setters =
                    {
                        new Setter(Visual.OpacityProperty, 0.0)
                    }
                }
            }
        };

        Log.Information("Animations created successfully.");
    }

    /// <summary>
    /// Sets up the visual for animation by applying initial transform and opacity.
    /// </summary>
    /// <param name="view">The visual to set up.</param>
    private void SetupViewForAnimation(Visual view)
    {
        view.RenderTransformOrigin = RelativePoint.TopLeft;
        TranslateTransform translateTransform = EnsureTransform<TranslateTransform>(view);
        translateTransform.Y = _sheetHeight + DisappearVerticalOffset;
        view.Opacity = DefaultBottomSheetVariables.DefaultOpacity;
        view.IsVisible = true;
    }

    /// <summary>
    /// Sets up the scrim for animation by applying initial opacity.
    /// </summary>
    /// <param name="view">The scrim visual to set up.</param>
    private void SetupScrimForAnimation(Visual view)
    {
        view.Opacity = 0.0;
        view.IsVisible = true;
    }

    /// <summary>
    /// Handles pointer pressed events on the scrim to dismiss the bottom sheet.
    /// </summary>
    /// <param name="sender">The sender of the event.</param>
    /// <param name="e">The event arguments.</param>
    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsDismissableOnScrimClick && ViewModel != null)
        {
            Log.Information("Scrim clicked: Executing HideCommand");
            ViewModel.HideCommand.Execute().Subscribe();
        }
    }

    /// <summary>
    /// Ensures a transform of the specified type is applied to the visual.
    /// </summary>
    /// <typeparam name="T">The type of transform.</typeparam>
    /// <param name="visual">The visual to apply the transform to.</param>
    /// <returns>The transform instance.</returns>
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