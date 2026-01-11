using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.NewContent.Views;

public partial class NewGroupChatView : UserControl
{
    public NewGroupChatView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

