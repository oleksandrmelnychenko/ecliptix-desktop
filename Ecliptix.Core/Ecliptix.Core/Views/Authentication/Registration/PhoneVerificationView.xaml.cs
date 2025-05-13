using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.ViewModels.Authentication.Registration;

namespace Ecliptix.Core.Views.Authentication.Registration;

public class PhoneVerificationView : UserControl
{
    public PhoneVerificationView(PhoneVerificationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}