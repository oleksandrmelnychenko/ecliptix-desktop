using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.ViewModels.Memberships;

namespace Ecliptix.Core.Views.Memberships.SignUp;

public partial class SignUpHostView : UserControl
{
    public SignUpHostView(SignUpHostViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}