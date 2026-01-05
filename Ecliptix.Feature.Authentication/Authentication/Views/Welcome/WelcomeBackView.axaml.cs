using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Feature.Authentication.Authentication.ViewModels.Welcome;

namespace Ecliptix.Feature.Authentication.Authentication.Views.Welcome;

public partial class WelcomeBackView : ReactiveUserControl<WelcomeBackViewModel>
{
    public WelcomeBackView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

