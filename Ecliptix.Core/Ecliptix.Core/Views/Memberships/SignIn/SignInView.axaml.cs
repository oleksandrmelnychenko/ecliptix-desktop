using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Memberships.SignIn;

namespace Ecliptix.Core.Views.Memberships.SignIn;

public partial class SignInView : ReactiveUserControl<SignInViewModel>
{
    private bool _handlersAttached;
    
    public SignInView()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetupEventHandlers();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TeardownEventHandlers();
    }
    
    private void SetupEventHandlers()
    {
        if (_handlersAttached)
            return;

        if (this.FindControl<Controls.HintedTextBox>("PasswordTextBox") is Controls.HintedTextBox passwordBox)
        {
            passwordBox.TextChanged += PasswordBox_TextChanged;
        }
        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
            return;

        if (this.FindControl<Controls.HintedTextBox>("PasswordTextBox") is Controls.HintedTextBox passwordBox)
        {
            passwordBox.TextChanged -= PasswordBox_TextChanged;
        }
        _handlersAttached = false;
    }

    private void PasswordBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is SignInViewModel vm && sender is Controls.HintedTextBox tb)
        {
            vm.UpdatePassword(tb.Text);
        }
    }
}