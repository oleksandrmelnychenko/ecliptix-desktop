using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Memberships;

namespace Ecliptix.Core.Views.Memberships;

public partial class WelcomeView : ReactiveUserControl<WelcomeViewModel>
{
    public WelcomeView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
