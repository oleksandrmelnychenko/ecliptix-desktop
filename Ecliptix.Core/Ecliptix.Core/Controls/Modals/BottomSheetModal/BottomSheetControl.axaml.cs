using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.ReactiveUI;
using Avalonia.VisualTree;
using ReactiveUI;
using Splat;

namespace Ecliptix.Core.Controls.Modals.BottomSheetModal;

public partial class BottomSheetControl : ReactiveUserControl<BottomSheetViewModel>
{
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
        InitializeDefaults();
    }

    private void InitializeDefaults()
    {
        ViewModel = Locator.Current.GetService<BottomSheetViewModel>();
        IsDismissableOnScrimClick = DefaultBottomSheetVariables.DefaultIsDismissableOnScrimClick;
        
        // Force ViewModel activation for event subscriptions
        if (ViewModel is IActivatableViewModel activatableViewModel)
        {
            activatableViewModel.Activator.Activate();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        SetupReactiveBindings();
    }

    private void SetupReactiveBindings()
    {
        this.WhenActivated(disposables =>
        {
            SetupDismissableBindings(disposables);
            SetupScrimColorBinding(disposables);
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

    private void OnScrimPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (IsDismissableOnScrimClick && ViewModel is not null)
        {
            ViewModel.IsVisible = false;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
    }
}