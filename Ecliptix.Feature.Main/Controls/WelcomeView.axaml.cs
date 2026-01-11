using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.Main.Controls;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
