using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls;
using Ecliptix.Core.ViewModels.Authentication.Registration;

namespace Ecliptix.Core.Views.Authentication.Registration;

public partial class PasswordConfirmationView : ReactiveUserControl<PasswordConfirmationViewModel>
{
    private bool _handlersAttached;

    public PasswordConfirmationView(PasswordConfirmationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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
            passwordBox.TextChanged += PasswordBox_TextChanged;
        }
        if (this.FindControl<HintedTextBox>("VerifyPasswordTextBox") is HintedTextBox verifyPasswordBox)
        {
            verifyPasswordBox.TextChanged += VerifyPasswordBox_TextChanged;
        }
        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
            return;
            
        if (this.FindControl<HintedTextBox>("PasswordTextBox") is HintedTextBox passwordBox)
        {
            passwordBox.TextChanged -= PasswordBox_TextChanged;
        }
        if (this.FindControl<HintedTextBox>("VerifyPasswordTextBox") is HintedTextBox verifyPasswordBox)
        {
            verifyPasswordBox.TextChanged -= VerifyPasswordBox_TextChanged;
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

