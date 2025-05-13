using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Views;

public partial class TitleBar : UserControl
{
    public static readonly StyledProperty<bool> DisableCloseButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(DisableCloseButton), true);

    public bool DisableCloseButton
    {
        get => GetValue(DisableCloseButtonProperty);
        set => SetValue(DisableCloseButtonProperty, value);
    }

    public static readonly StyledProperty<bool> DisableMinimizeButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(DisableMinimizeButton), true);

    public bool DisableMinimizeButton
    {
        get => GetValue(DisableMinimizeButtonProperty);
        set => SetValue(DisableMinimizeButtonProperty, value);
    }

    public static readonly StyledProperty<bool> DisableMaximizeButtonProperty =
        AvaloniaProperty.Register<TitleBar, bool>(nameof(DisableMaximizeButton), true);

    public bool DisableMaximizeButton
    {
        get => GetValue(DisableMaximizeButtonProperty);
        set => SetValue(DisableMaximizeButtonProperty, value);
    }
    
    public TitleBar()
    {
        InitializeComponent();

        this.FindControl<Panel>("PART_Root")!
            .PointerPressed += OnRootPointerPressed;

        this.FindControl<Button>("PART_Close")!
            .Click += (_, __) => Window?.Close();

        this.FindControl<Button>("PART_Minimize")!
            .Click += (_, __) =>
        {
            if (Window != null)
                Window.WindowState = WindowState.Minimized;
        };

        this.FindControl<Button>("PART_Maximize")!
            .Click += (_, __) =>
        {
            if (Window != null)
                Window.WindowState = Window.WindowState == WindowState.Maximized
                    ? WindowState.Normal
                    : WindowState.Maximized;
        };
    }

    private Window? Window => VisualRoot as Window;

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Window?.BeginMoveDrag(e);
    }
}