using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Core;

namespace Ecliptix.Core.Features.Main.Views;

public partial class MasterView : UserControl
{
    public MasterView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
