using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.Chats.Views;

public partial class ChatsView : UserControl
{
    public ChatsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
