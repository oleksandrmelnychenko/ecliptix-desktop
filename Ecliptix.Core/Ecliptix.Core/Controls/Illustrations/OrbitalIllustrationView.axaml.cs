using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Controls.Illustrations;

public partial class OrbitalIllustrationView : UserControl
{
    public OrbitalIllustrationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
