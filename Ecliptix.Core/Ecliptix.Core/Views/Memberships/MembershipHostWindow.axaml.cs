using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Services;
using Ecliptix.Core.ViewModels.Authentication;

namespace Ecliptix.Core.Views.Memberships;

public partial class MembershipHostWindow : ReactiveWindow<MembershipHostWindowModel>
{
    private const string NotificationContainerControl = "NotificationContainer";
    
    public MembershipHostWindow()
    {
        AvaloniaXamlLoader.Load(this);
        IconService.SetIconForWindow(this);
    #if DEBUG
            this.AttachDevTools();
    #endif
    }
}
