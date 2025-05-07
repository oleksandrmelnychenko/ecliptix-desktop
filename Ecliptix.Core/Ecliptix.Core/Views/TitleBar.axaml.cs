using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Views;

public partial class TitleBar : UserControl
{
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
                Window.WindowState = (Window.WindowState == WindowState.Maximized)
                    ? WindowState.Normal
                    : WindowState.Maximized;
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private Window? Window => this.VisualRoot as Window;

    private void OnRootPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            Window?.BeginMoveDrag(e);
    }
}