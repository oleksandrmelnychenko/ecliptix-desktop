using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls;
using Ecliptix.Core.ViewModels.Authentication.Registration;
using Ecliptix.Core.ViewModels.Memberships.SignUp;

namespace Ecliptix.Core.Views.Memberships.SignUp;

public partial class PasswordConfirmationView : ReactiveUserControl<PasswordConfirmationViewModel>
{
    private bool _handlersAttached;

    public PasswordConfirmationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
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
            
        if (this.FindControl<HintedTextBox>("PasswordTextBox") is HintedTextBox passwordBox)
        {
        }
        if (this.FindControl<HintedTextBox>("VerifyPasswordTextBox") is HintedTextBox verifyPasswordBox)
        {
        }
        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
            return;
            
        if (this.FindControl<HintedTextBox>("PasswordTextBox") is HintedTextBox passwordBox)
        {
        }
        if (this.FindControl<HintedTextBox>("VerifyPasswordTextBox") is HintedTextBox verifyPasswordBox)
        {
        }
        _handlersAttached = false;
    }

    private void PasswordBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is PasswordConfirmationViewModel vm && sender is HintedTextBox tb)
        {
            vm.UpdatePassword(tb.Text);
        }
    }

    private void VerifyPasswordBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is PasswordConfirmationViewModel vm && sender is HintedTextBox tb)
        {
            vm.UpdateVerifyPassword(tb.Text);
        }
    }
}

