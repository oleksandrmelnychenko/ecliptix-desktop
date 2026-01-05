using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Feature.Authentication.Authentication.ViewModels.Welcome;

namespace Ecliptix.Feature.Authentication.Authentication.Views.Welcome;

public partial class WelcomeView : ReactiveUserControl<WelcomeViewModel>
{
    public WelcomeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
