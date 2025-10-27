using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;

namespace Ecliptix.Core.Features.Authentication.Views.Hosts;

public partial class AuthenticationView : ReactiveUserControl<AuthenticationViewModel>
{
    public AuthenticationView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
