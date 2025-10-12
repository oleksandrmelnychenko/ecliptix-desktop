using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.EventArgs;
using Ecliptix.Core.Features.Authentication.ViewModels.PasswordRecovery;

namespace Ecliptix.Core.Features.Authentication.Views.PasswordRecovery;

public partial class ForgotPasswordResetView : ReactiveUserControl<ForgotPasswordResetViewModel>
{
    private bool _handlersAttached;

    public ForgotPasswordResetView()
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

        if (this.FindControl<HintedTextBox>("NewPasswordTextBox") is HintedTextBox newPasswordBox)
        {
            newPasswordBox.SecureKeyCharactersAdded += OnNewPasswordCharactersAdded;
            newPasswordBox.SecureKeyCharactersRemoved += OnNewPasswordCharactersRemoved;
            newPasswordBox.KeyDown += OnNewPasswordKeyDown;
            newPasswordBox.CharacterRejected += OnCharacterRejected;
        }

        if (this.FindControl<HintedTextBox>("ConfirmPasswordTextBox") is HintedTextBox confirmPasswordBox)
        {
            confirmPasswordBox.SecureKeyCharactersAdded += OnConfirmPasswordCharactersAdded;
            confirmPasswordBox.SecureKeyCharactersRemoved += OnConfirmPasswordCharactersRemoved;
            confirmPasswordBox.KeyDown += OnConfirmPasswordKeyDown;
            confirmPasswordBox.CharacterRejected += OnCharacterRejected;
        }

        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
            return;

        if (this.FindControl<HintedTextBox>("NewPasswordTextBox") is HintedTextBox newPasswordBox)
        {
            newPasswordBox.SecureKeyCharactersAdded -= OnNewPasswordCharactersAdded;
            newPasswordBox.SecureKeyCharactersRemoved -= OnNewPasswordCharactersRemoved;
            newPasswordBox.KeyDown -= OnNewPasswordKeyDown;
            newPasswordBox.CharacterRejected -= OnCharacterRejected;
        }

        if (this.FindControl<HintedTextBox>("ConfirmPasswordTextBox") is HintedTextBox confirmPasswordBox)
        {
            confirmPasswordBox.SecureKeyCharactersAdded -= OnConfirmPasswordCharactersAdded;
            confirmPasswordBox.SecureKeyCharactersRemoved -= OnConfirmPasswordCharactersRemoved;
            confirmPasswordBox.KeyDown -= OnConfirmPasswordKeyDown;
            confirmPasswordBox.CharacterRejected -= OnCharacterRejected;
        }

        _handlersAttached = false;
    }

    private void OnNewPasswordCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not ForgotPasswordResetViewModel vm || sender is not HintedTextBox tb) return;
        vm.InsertNewPasswordChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentNewPasswordLength);
    }

    private void OnNewPasswordCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not ForgotPasswordResetViewModel vm || sender is not HintedTextBox tb) return;
        vm.RemoveNewPasswordChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentNewPasswordLength);
    }

    private void OnConfirmPasswordCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not ForgotPasswordResetViewModel vm || sender is not HintedTextBox tb) return;
        vm.InsertConfirmPasswordChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentConfirmPasswordLength);
    }

    private void OnConfirmPasswordCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not ForgotPasswordResetViewModel vm || sender is not HintedTextBox tb) return;
        vm.RemoveConfirmPasswordChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentConfirmPasswordLength);
    }

    private void OnNewPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;

        HintedTextBox? confirmPasswordBox = this.FindControl<HintedTextBox>("ConfirmPasswordTextBox");
        confirmPasswordBox?.Focus();
        e.Handled = true;
    }

    private void OnConfirmPasswordKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;

        if (DataContext is not ForgotPasswordResetViewModel vm) return;

        _ = vm.HandleEnterKeyPressAsync();
        e.Handled = true;
    }

    private void OnCharacterRejected(object? sender, CharacterRejectedEventArgs e)
    {
        if (DataContext is not ForgotPasswordResetViewModel vm || sender is not HintedTextBox tb) return;

        string localizedMessage = vm.GetLocalizedWarningMessage(e.WarningType);
        tb.WarningText = localizedMessage;
        tb.HasWarning = true;
    }
}
