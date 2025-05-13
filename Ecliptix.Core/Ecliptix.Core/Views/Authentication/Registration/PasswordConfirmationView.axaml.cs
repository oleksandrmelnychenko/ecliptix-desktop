using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.ViewModels.Authentication.Registration;

namespace Ecliptix.Core.Views.Authentication.Registration;

public partial class PasswordConfirmationView : UserControl
{
    public PasswordConfirmationView(PasswordConfirmationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}