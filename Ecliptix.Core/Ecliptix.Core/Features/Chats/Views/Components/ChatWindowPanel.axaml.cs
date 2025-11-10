using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Chats.Views.Components;

public partial class ChatWindowPanel : UserControl
{
    public ChatWindowPanel()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
