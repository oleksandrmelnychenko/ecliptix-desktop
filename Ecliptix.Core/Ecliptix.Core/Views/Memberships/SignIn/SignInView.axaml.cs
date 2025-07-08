using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Authentication;

namespace Ecliptix.Core.Views.Memberships.SignIn;

public partial class SignInView : ReactiveUserControl<SignInViewModel>
{
    public SignInView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}