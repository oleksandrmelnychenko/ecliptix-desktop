using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Linq;

namespace Ecliptix.Core.Views.Memberships.Components;

public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<bool> DisableCloseButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(DisableCloseButton), true);

    public static readonly StyledProperty<bool> DisableMinimizeButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(DisableMinimizeButton), true);

    public static readonly StyledProperty<bool> DisableMaximizeButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(DisableMaximizeButton), true);

    public ReactiveCommand<Unit, Unit> CloseCommand { get; }
    public ReactiveCommand<Unit, Unit> MinimizeCommand { get; }
    public ReactiveCommand<Unit, Unit> MaximizeCommand { get; }

    private IDisposable? _pointerPressedSubscription;

    public TitleBar()
    {
        InitializeComponent();

        CloseCommand = ReactiveCommand.Create(
            () => Window?.Close(),
            this.WhenAnyValue(x => x.DisableCloseButton).Select(disable => !disable)
        );

        MinimizeCommand = ReactiveCommand.Create(
            () =>
            {
                if (Window != null)
                    Window.WindowState = WindowState.Minimized;
            },
            this.WhenAnyValue(x => x.DisableMinimizeButton).Select(disable => !disable)
        );

        MaximizeCommand = ReactiveCommand.Create(
            () =>
            {
                if (Window != null)
                    Window.WindowState = Window.WindowState == WindowState.Maximized
                        ? WindowState.Normal
                        : WindowState.Maximized;
            },
            this.WhenAnyValue(x => x.DisableMaximizeButton).Select(disable => !disable)
        );

        Panel? rootPanel = this.FindControl<Panel>("PART_Root");
        if (rootPanel != null)
        {
            _pointerPressedSubscription = Observable.FromEventPattern<PointerPressedEventArgs>(
                h => rootPanel.PointerPressed += h,
                h => rootPanel.PointerPressed -= h
            ).Subscribe(e =>
            {
                if (e.EventArgs.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
                    Window?.BeginMoveDrag(e.EventArgs);
            });
        }

        this.Unloaded += (s, e) => _pointerPressedSubscription?.Dispose();
    }

    public bool DisableCloseButton
    {
        get => GetValue(DisableCloseButtonProperty);
        set => SetValue(DisableCloseButtonProperty, value);
    }

    public bool DisableMinimizeButton
    {
        get => GetValue(DisableMinimizeButtonProperty);
        set => SetValue(DisableMinimizeButtonProperty, value);
    }

    public bool DisableMaximizeButton
    {
        get => GetValue(DisableMaximizeButtonProperty);
        set => SetValue(DisableMaximizeButtonProperty, value);
    }

    private Window? Window => VisualRoot as Window;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}