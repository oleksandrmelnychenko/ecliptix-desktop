using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Authentication.ViewModels.Welcome;

namespace Ecliptix.Core.Features.Authentication.Views.Welcome;

public partial class WelcomeBackView : ReactiveUserControl<WelcomeBackViewModel>
{
    public WelcomeBackView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}

