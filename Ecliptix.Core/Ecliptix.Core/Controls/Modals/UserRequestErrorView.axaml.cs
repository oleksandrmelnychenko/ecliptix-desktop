using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Modals;

public partial class UserRequestErrorView : ReactiveUserControl<RedirectNotificationViewModel>
{
    public UserRequestErrorView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
