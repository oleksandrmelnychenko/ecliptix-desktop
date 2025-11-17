using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Views.Memberships.Components.Platform.Windows;

public partial class WindowsTitleBarButtons : UserControl
{
    public WindowsTitleBarButtons()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

}

