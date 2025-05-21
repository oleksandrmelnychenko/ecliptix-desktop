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
    public SignInView()
    {
        InitializeComponent();
    }

    public SignInView(SignInViewModel viewModel) : this()
    {
        DataContext = viewModel;
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

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        TeardownEventHandlers(); 
        SetupEventHandlers();
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
                if (passwordBoxViaFind.MainTextBox != null)
                {
                    passwordBoxViaFind.MainTextBox.TextChanged += PasswordBox_MainTextBox_TextChanged;
                }
            }
        }
    }

    private void TeardownEventHandlers()
    {
        if (PasswordTextBox != null && PasswordTextBox.MainTextBox != null)
        {
            PasswordTextBox.MainTextBox.TextChanged -= PasswordBox_MainTextBox_TextChanged;
            Debug.WriteLine("SignInView: Detached TextChanged from this.PasswordTextBox.MainTextBox.");
        }

        HintedTextBox? passwordBoxViaFind = this.FindControl<HintedTextBox>("PasswordTextBox");
        if (passwordBoxViaFind is { MainTextBox: not null })
        {
            passwordBoxViaFind.MainTextBox.TextChanged -= PasswordBox_MainTextBox_TextChanged;
        }
    }

    private void PasswordBox_MainTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (DataContext is SignInViewModel vm && sender is TextBox mainPasswordTextBox)
        {
            vm.UpdatePassword(mainPasswordTextBox.Text);
        }
    }
}