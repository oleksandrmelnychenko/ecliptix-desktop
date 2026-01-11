using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.Chats.Views;

public partial class MessageActionMenu : UserControl
{
    public MessageActionMenu()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public async Task AnimateCloseAsync()
    {
        Border? border = this.FindControl<Border>("MainBorder");

        if (border != null)
        {
            border.Classes.Remove("animatable");
            border.Classes.Add("closing");
        }
        await Task.Delay(150);
    }
}

