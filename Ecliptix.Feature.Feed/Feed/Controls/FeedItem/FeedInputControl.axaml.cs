using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Ecliptix.Feature.Feed.Feed.Controls.FeedItem;

public partial class FeedInputControl : UserControl
{
    public static readonly StyledProperty<bool> IsExpandProperty =
       AvaloniaProperty.Register<FeedInputControl, bool>(nameof(IsExpand), defaultValue: default);

    public bool IsExpand
    {
        get => GetValue(IsExpandProperty);
        set => SetValue(IsExpandProperty, value);
    }

    public static readonly StyledProperty<ICommand> ToggleExpandCommandProperty =
       AvaloniaProperty.Register<FeedInputControl, ICommand>(nameof(ToggleExpandCommand));

    public ICommand ToggleExpandCommand
    {
        get => GetValue(ToggleExpandCommandProperty);
        set => SetValue(ToggleExpandCommandProperty, value);
    }

    public FeedInputControl()
    {
        InitializeComponent();

        ToggleExpandCommand = ReactiveUI.ReactiveCommand.Create(() =>
        {
            IsExpand = !IsExpand;
            SwitchFocusBetweenInputs();
        });
    }

    private void TextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (e.Source is TextBox textBox && !string.IsNullOrEmpty(textBox.Text) && textBox.Text.Length >= 60)
        {
            IsExpand = true;
            SwitchFocusBetweenInputs();
        }
    }

    private void SwitchFocusBetweenInputs()
    {
        if (IsExpand)
        {
            extendedInput.Focus();
            int cursorPosition = simpleInput?.Text?.Length ?? 0;

            extendedInput.SelectionStart = cursorPosition;
            extendedInput.SelectionEnd = cursorPosition;
        }
        else
        {
            simpleInput.Focus();
            int cursorPosition = extendedInput?.Text?.Length ?? 0;

            simpleInput.SelectionStart = cursorPosition;
            simpleInput.SelectionEnd = cursorPosition;
        }
    }
}
