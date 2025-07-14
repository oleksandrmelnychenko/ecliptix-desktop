using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Authentication;

namespace Ecliptix.Core.Views.Memberships;

public partial class MembershipHostWindow : ReactiveWindow<MembershipHostWindowModel>
{
    public MembershipHostWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}