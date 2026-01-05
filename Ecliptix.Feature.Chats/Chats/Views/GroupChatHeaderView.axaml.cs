using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.Chats.Chats.Views;

public partial class GroupChatHeaderView : UserControl
{
    public GroupChatHeaderView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

