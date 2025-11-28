using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Features.NewContent.Views;

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

