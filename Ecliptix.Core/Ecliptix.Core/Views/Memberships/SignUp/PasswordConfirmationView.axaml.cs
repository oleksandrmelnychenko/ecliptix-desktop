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
            passwordBox.SecureKeyCharactersAdded += OnPasswordCharactersAdded;
            passwordBox.SecureKeyCharactersRemoved += OnPasswordCharactersRemoved;
        }
        if (this.FindControl<HintedTextBox>("VerifyPasswordTextBox") is HintedTextBox verifyPasswordBox)
        {
            verifyPasswordBox.SecureKeyCharactersAdded += OnVerifyPasswordCharactersAdded;
            verifyPasswordBox.SecureKeyCharactersRemoved += OnVerifyPasswordCharactersRemoved;
        }
        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
            return;
        
        if (this.FindControl<HintedTextBox>("PasswordTextBox") is HintedTextBox passwordBox)
        {
            passwordBox.SecureKeyCharactersAdded -= OnPasswordCharactersAdded;
            passwordBox.SecureKeyCharactersRemoved -= OnPasswordCharactersRemoved;
        }
        if (this.FindControl<HintedTextBox>("VerifyPasswordTextBox") is HintedTextBox verifyPasswordBox)
        {
            verifyPasswordBox.SecureKeyCharactersAdded -= OnVerifyPasswordCharactersAdded;
            verifyPasswordBox.SecureKeyCharactersRemoved -= OnVerifyPasswordCharactersRemoved;
        }
        _handlersAttached = false;
    }

    private void OnPasswordCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not PasswordConfirmationViewModel vm || sender is not HintedTextBox tb) return;
        vm.InsertPasswordChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentPasswordLength);
    }

    private void OnPasswordCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not PasswordConfirmationViewModel vm || sender is not HintedTextBox tb) return;
        vm.RemovePasswordChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentPasswordLength);
    }

    private void OnVerifyPasswordCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not PasswordConfirmationViewModel vm || sender is not HintedTextBox tb) return;
        vm.InsertVerifyPasswordChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentVerifyPasswordLength);
    }
    
    private void OnVerifyPasswordCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not PasswordConfirmationViewModel vm || sender is not HintedTextBox tb) return;
        vm.RemoveVerifyPasswordChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentVerifyPasswordLength);
    }
}

