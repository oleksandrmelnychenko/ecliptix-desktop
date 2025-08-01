using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Memberships.SignUp;

namespace Ecliptix.Core.Views.Memberships.SignUp;

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