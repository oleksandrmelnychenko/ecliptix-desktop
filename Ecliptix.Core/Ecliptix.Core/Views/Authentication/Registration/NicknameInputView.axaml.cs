using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Authentication.Registration;


namespace Ecliptix.Core.Views.Authentication.Registration;

public partial class NicknameInputView :  ReactiveUserControl<NicknameInputViewModel>
{
    
    public NicknameInputView(NicknameInputViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}