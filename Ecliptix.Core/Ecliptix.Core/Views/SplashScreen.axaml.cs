using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels;

namespace Ecliptix.Core.Views;

public partial class SplashScreen :  ReactiveWindow<SplashScreenViewModel>
{
    public SplashScreen()
    {
        InitializeComponent();
    }
}
