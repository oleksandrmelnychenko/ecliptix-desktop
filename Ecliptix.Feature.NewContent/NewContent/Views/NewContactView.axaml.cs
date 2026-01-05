using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Feature.NewContent.NewContent.Views;

public partial class NewContactView : UserControl
{
    public NewContactView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

