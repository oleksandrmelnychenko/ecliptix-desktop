using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Controls;
using Ecliptix.Core.ViewModels.Authentication.Registration;

namespace Ecliptix.Core.Views.Authentication.Registration;

public partial class PasswordConfirmationView : UserControl
{
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        TeardownEventHandlers();
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        if (DataContext is not PasswordConfirmationViewModel vm) return;

        HintedTextBox? passwordBox = this.FindControl<HintedTextBox>("PasswordTextBox");
        if (passwordBox?.MainTextBox != null)
        {
            passwordBox.MainTextBox.TextChanged += PasswordBox_TextChanged;
        }

        HintedTextBox? verifyPasswordBox = this.FindControl<HintedTextBox>("VerifyPasswordTextBox");
        if (verifyPasswordBox?.MainTextBox != null)
        {
            verifyPasswordBox.MainTextBox.TextChanged += VerifyPasswordBox_TextChanged;
        }
    }
    private void TeardownEventHandlers()
    {
        HintedTextBox? passwordBox = this.FindControl<HintedTextBox>("PasswordTextBox");
        if (passwordBox?.MainTextBox != null)
        {
            passwordBox.MainTextBox.TextChanged -= PasswordBox_TextChanged;
        }

        HintedTextBox? verifyPasswordBox = this.FindControl<HintedTextBox>("VerifyPasswordTextBox");
        if (verifyPasswordBox?.MainTextBox != null)
        {
            verifyPasswordBox.MainTextBox.TextChanged -= VerifyPasswordBox_TextChanged;
        }
    }

    private void PasswordBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is PasswordConfirmationViewModel vm && sender is HintedTextBox pb)
        {
            vm.UpdatePassword(pb.MainTextBox.Text);
        }
    }

    private void VerifyPasswordBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is PasswordConfirmationViewModel vm && sender is HintedTextBox pb)
        {
            vm.UpdateVerifyPassword(pb.MainTextBox.Text);
        }
    }
}

