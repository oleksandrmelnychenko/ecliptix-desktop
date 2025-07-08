using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels;
using Ecliptix.Core.ViewModels.Memberships;

namespace Ecliptix.Core.Views.Memberships.Components.Splash;

public partial class SplashWindow : ReactiveWindow<SplashWindowViewModel>
{
    public SplashWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}