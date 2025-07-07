using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels;
using ReactiveUI;

namespace Ecliptix.Core.Views;

public partial class SplashScreen :  ReactiveWindow<SplashScreenViewModel>
{
    public SplashScreen()
    {
        this.WhenActivated(disposables =>
        {
            
        });
        AvaloniaXamlLoader.Load(this);
    }
}