using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Main.Views.Components;

public partial class WelcomeView : UserControl
{
    public WelcomeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
