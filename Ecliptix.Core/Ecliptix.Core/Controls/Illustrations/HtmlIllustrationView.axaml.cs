using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Controls.Illustrations;

public partial class HtmlIllustrationView : UserControl
{
    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<HtmlIllustrationView, string?>(nameof(Url));

    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    public HtmlIllustrationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
