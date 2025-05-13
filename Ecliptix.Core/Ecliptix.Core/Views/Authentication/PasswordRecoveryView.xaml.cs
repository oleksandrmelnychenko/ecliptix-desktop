using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Ecliptix.Core.Views.Authentication;

public class PasswordRecoveryView : UserControl
{
    public PasswordRecoveryView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}