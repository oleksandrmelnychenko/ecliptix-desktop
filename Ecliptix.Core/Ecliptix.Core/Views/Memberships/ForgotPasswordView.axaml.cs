using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Views.Memberships;

public partial class ForgotPasswordView : UserControl
{
    public ForgotPasswordView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}