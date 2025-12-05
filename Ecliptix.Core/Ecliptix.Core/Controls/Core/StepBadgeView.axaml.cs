using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Controls.Core;

public partial class StepBadgeView : UserControl
{

    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<StepBadgeView, string>(nameof(Text), defaultValue: "Step 0 of 0");

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StepBadgeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

