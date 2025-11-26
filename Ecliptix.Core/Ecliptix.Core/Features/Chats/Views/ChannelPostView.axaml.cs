using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Chats.Views;

public partial class ChannelPostView : UserControl
{
    public ChannelPostView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

