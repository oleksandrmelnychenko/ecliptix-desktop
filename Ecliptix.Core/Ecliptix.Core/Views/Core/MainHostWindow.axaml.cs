using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Services;
using Ecliptix.Core.Services.Core;

namespace Ecliptix.Core.Views.Core;

public partial class MainHostWindow : Window
{
    public MainHostWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);
    }
}
