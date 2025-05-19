using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.ViewModels.Authentication.Registration;


namespace Ecliptix.Core.Views.Authentication.Registration;

public partial class NicknameInputView : UserControl
{
    
    public NicknameInputView(NicknameInputViewModel viewModel)
    {
        InitializeComponent();
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    
}