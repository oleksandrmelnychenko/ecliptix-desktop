using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;

namespace Ecliptix.Core.Features.Authentication.Views.Registration;

public partial class CompleteProfileView : ReactiveUserControl<CompleteProfileViewModel>
{
    public CompleteProfileView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

