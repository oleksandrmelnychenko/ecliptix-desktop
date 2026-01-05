using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.Chats.Chats.Views;

public partial class ChatHeaderView : UserControl
{
    public ChatHeaderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

