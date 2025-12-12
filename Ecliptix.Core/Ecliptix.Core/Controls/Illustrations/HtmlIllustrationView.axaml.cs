using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using AvaloniaWebView;

namespace Ecliptix.Core.Controls.Illustrations;

public partial class HtmlIllustrationView : UserControl
{
    private WebView? _webView;

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
        _webView = this.FindControl<WebView>("WebViewControl");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (!string.IsNullOrEmpty(Url))
        {
            LoadContent(Url);
        }
    }

    private void OnUrlChanged(string? url)
    {
        if (!string.IsNullOrEmpty(url))
        {
            LoadContent(url);
        }
    }

    private void LoadContent(string url)
    {
        if (_webView == null)
        {
            return;
        }

        try
        {
            if (url.StartsWith("avares://"))
            {
                Uri uri = new Uri(url);
                using Stream stream = AssetLoader.Open(uri);
                using StreamReader reader = new StreamReader(stream);
                string htmlContent = reader.ReadToEnd();

                Dispatcher.UIThread.Post(() =>
                {
                    _webView.HtmlContent = htmlContent;

                });
            }
            else
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (Uri.TryCreate(url, UriKind.RelativeOrAbsolute, out Uri? resultUri))
                    {
                        _webView.Url = resultUri;
                    }
                });
            }
        }
        catch (Exception ex)
        {
            string errorHtml = $"<html><body><h3>Error</h3><p>{ex.Message}</p></body></html>";
            _webView.HtmlContent = errorHtml;
        }
    }
}
