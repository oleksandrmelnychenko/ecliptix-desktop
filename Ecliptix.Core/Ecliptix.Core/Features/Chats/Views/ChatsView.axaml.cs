using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Chats.Views;

public partial class ChatsView : UserControl
{
    public ChatsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
