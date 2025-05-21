using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.ViewModels.Authentication;
using Ecliptix.Core.ViewModels.Memberships;

namespace Ecliptix.Core.Views.Authentication;

public partial class SignInView : UserControl
{
    public SignInView(SignInViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}