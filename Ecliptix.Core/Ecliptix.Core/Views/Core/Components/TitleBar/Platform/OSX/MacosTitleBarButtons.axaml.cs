using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Views.Core.Components.TitleBar.Platform.OSX;

public partial class MacosTitleBarButtons : UserControl
{
    public MacosTitleBarButtons()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

