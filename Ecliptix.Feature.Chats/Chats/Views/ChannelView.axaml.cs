using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.Chats.Chats.Views;

public partial class ChannelView : UserControl
{
    public ChannelView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

