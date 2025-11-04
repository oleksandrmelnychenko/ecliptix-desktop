using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.Main.Views;

public partial class MasterView : UserControl
{
    public MasterView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
