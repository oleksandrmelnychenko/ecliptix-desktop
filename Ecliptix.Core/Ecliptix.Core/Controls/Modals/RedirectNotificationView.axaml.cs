using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class RedirectNotificationView : ReactiveUserControl<RedirectNotificationViewModel>
{
    public RedirectNotificationView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}