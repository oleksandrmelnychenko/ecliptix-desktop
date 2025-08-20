using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;

namespace Ecliptix.Core.Features.Authentication.Views.Registration;

public partial class VerificationCodeEntryView : ReactiveUserControl<VerifyOtpViewModel>
{
    public VerificationCodeEntryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}