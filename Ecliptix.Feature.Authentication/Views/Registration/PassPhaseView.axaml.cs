using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Feature.Authentication.ViewModels.Registration;

namespace Ecliptix.Feature.Authentication.Views.Registration;

public partial class PassPhaseView : ReactiveUserControl<PassPhaseViewModel>
{
    public PassPhaseView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
