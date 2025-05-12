using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.ViewModels.Memberships;

namespace Ecliptix.Core.Views.Memberships.SignUp;

public partial class ApplyVerificationCodeView : UserControl
{
    public ApplyVerificationCodeView(ApplyVerificationCodeViewModel viewmodel)
    {
        InitializeComponent();
        DataContext = viewmodel;
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}