using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Chats.Views.Components;

public partial class ConversationItem : UserControl
{
    public ConversationItem()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
