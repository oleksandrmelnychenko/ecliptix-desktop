using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Feature.Authentication.ViewModels.Welcome;

namespace Ecliptix.Feature.Authentication.Views.Welcome;

public partial class WelcomeBackView : ReactiveUserControl<WelcomeBackViewModel>
{
    public WelcomeBackView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

