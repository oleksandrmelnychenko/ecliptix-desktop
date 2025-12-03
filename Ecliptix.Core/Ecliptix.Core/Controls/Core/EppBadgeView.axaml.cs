using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public partial class EppBadgeView : ReactiveUserControl<EppBadgeViewModel>
{
    public EppBadgeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

