using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.Chats.Chats.Views;

public partial class ChatInputView : UserControl
{
    public ChatInputView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

