using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;

namespace Ecliptix.Core.Controls.Illustrations;

public partial class HtmlIllustrationView : UserControl
{
    private Border? _contentBorder;
    private TextBlock? _textBlock;

    public static readonly StyledProperty<string?> UrlProperty =
        AvaloniaProperty.Register<HtmlIllustrationView, string?>(nameof(Url));

    public string? Url
    {
        get => GetValue(UrlProperty);
        set => SetValue(UrlProperty, value);
    }

    static HtmlIllustrationView()
    {
        UrlProperty.Changed.AddClassHandler<HtmlIllustrationView>((x, e) => x.OnUrlChanged(e.NewValue as string));
    }

    public HtmlIllustrationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _contentBorder = this.FindControl<Border>("ContentBorder");
        _textBlock = this.FindControl<TextBlock>("StatusText");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (!string.IsNullOrEmpty(Url))
        {
            LoadHtmlContent(Url);
        }
    }

    private void OnUrlChanged(string? url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            LoadHtmlContent(url);
        }
    }

    private void LoadHtmlContent(string url)
    {
        try
        {
            if (url.StartsWith("avares://"))
            {
                Uri uri = new Uri(url);
                using Stream stream = AssetLoader.Open(uri);
                using StreamReader reader = new StreamReader(stream);
                string htmlContent = reader.ReadToEnd();

                if (_textBlock != null)
                {
                    _textBlock.Text = $"HTML Loaded ({htmlContent.Length} bytes)\n{url}";
                }
            }
        }
        catch (Exception ex)
        {
            if (_textBlock != null)
            {
                _textBlock.Text = $"Error: {ex.Message}";
            }
        }
    }
}
