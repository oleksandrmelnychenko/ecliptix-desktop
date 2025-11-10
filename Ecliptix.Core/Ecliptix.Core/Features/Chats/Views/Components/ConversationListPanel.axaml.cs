using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Chats.Views.Components;

public partial class ConversationListPanel : UserControl
{
    public ConversationListPanel()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
