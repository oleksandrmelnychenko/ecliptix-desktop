using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Memberships;

namespace Ecliptix.Core.Views.Memberships;

public partial class MembershipHostWindow : ReactiveWindow<MembershipHostWindowModel>
{
    public MembershipHostWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);
    #if DEBUG
            this.AttachDevTools();
    #endif
    }
}
