using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public partial class NetworkBadgeView : ReactiveUserControl<NetworkBadgeViewModel>
{
    public NetworkBadgeView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

