using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls;
using Ecliptix.Core.ViewModels.Authentication;
using System.Diagnostics;

namespace Ecliptix.Core.Views.Authentication;

public partial class SignInView : ReactiveUserControl<SignInViewModel>
{
    public SignInView(SignInViewModel viewModel)
    {
        DataContext = viewModel;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Debug.WriteLine("SignInView: OnAttachedToVisualTree called.");
        SetupEventHandlers();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Debug.WriteLine("SignInView: OnDetachedFromVisualTree called.");
        TeardownEventHandlers();
    }

    private void SetupEventHandlers()
    {
        if (PasswordTextBox != null)
        {
            if (PasswordTextBox.MainTextBox != null)
            {
                PasswordTextBox.MainTextBox.TextChanged += PasswordBox_MainTextBox_TextChanged;
            }
        }
        else
        {
            HintedTextBox? passwordBoxViaFind = this.FindControl<HintedTextBox>("PasswordTextBox");
            if (passwordBoxViaFind != null)
            {
                passwordBoxViaFind.TextChanged += PasswordBox_MainTextBox_TextChanged;
            }
        }
    }

    private void TeardownEventHandlers()
    {
        HintedTextBox? passwordBoxViaFind = this.FindControl<HintedTextBox>("PasswordTextBox");
        passwordBoxViaFind.TextChanged -= PasswordBox_MainTextBox_TextChanged;
    }

    private void PasswordBox_MainTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is SignInViewModel vm && sender is HintedTextBox mainPasswordTextBox)
        {
            vm.UpdatePassword(mainPasswordTextBox.Text);
        }
    }
}