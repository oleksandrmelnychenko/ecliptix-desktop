using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.ViewModels.Authentication.Registration;

namespace Ecliptix.Core.Views.Authentication.Registration;

public partial class VerificationCodeEntryView : UserControl
{
    public VerificationCodeEntryView(VerificationCodeEntryViewModel viewmodel)
    {
        InitializeComponent();
        DataContext = viewmodel;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}