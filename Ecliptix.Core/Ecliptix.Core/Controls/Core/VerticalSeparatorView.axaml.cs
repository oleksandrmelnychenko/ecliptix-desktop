using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public partial class VerticalSeparatorView : ReactiveUserControl<VerticalSeparatorViewModel>
{
    public VerticalSeparatorView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

