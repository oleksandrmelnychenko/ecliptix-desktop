using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Authentication.ViewModels.Welcome;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.Views.Welcome;

public partial class WelcomeView : ReactiveUserControl<WelcomeViewModel>
{
    public WelcomeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
