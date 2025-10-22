using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.EventArgs;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;

namespace Ecliptix.Core.Features.Authentication.Views.Registration;

public partial class SecureKeyConfirmationView : ReactiveUserControl<SecureKeyVerifierViewModel>
{
    private bool _handlersAttached;

    public SecureKeyConfirmationView()
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
        {
            return;
        }

        if (this.FindControl<HintedTextBox>("SecureKeyTextBox") is HintedTextBox secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded += OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved += OnSecureKeyCharactersRemoved;
            secureKeyBox.KeyDown += OnSecureKeyTextBoxKeyDown;
            secureKeyBox.CharacterRejected += OnCharacterRejected;
        }
        if (this.FindControl<HintedTextBox>("VerifySecureKeyTextBox") is HintedTextBox verifySecureKeyBox)
        {
            verifySecureKeyBox.SecureKeyCharactersAdded += OnVerifySecureKeyCharactersAdded;
            verifySecureKeyBox.SecureKeyCharactersRemoved += OnVerifySecureKeyCharactersRemoved;
            verifySecureKeyBox.KeyDown += OnSecureKeyTextBoxKeyDown;
            verifySecureKeyBox.CharacterRejected += OnCharacterRejected;
        }
        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedTextBox>("SecureKeyTextBox") is HintedTextBox secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded -= OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved -= OnSecureKeyCharactersRemoved;
            secureKeyBox.KeyDown -= OnSecureKeyTextBoxKeyDown;
            secureKeyBox.CharacterRejected -= OnCharacterRejected;
        }
        if (this.FindControl<HintedTextBox>("VerifySecureKeyTextBox") is HintedTextBox verifySecureKeyBox)
        {
            verifySecureKeyBox.SecureKeyCharactersAdded -= OnVerifySecureKeyCharactersAdded;
            verifySecureKeyBox.SecureKeyCharactersRemoved -= OnVerifySecureKeyCharactersRemoved;
            verifySecureKeyBox.KeyDown -= OnSecureKeyTextBoxKeyDown;
            verifySecureKeyBox.CharacterRejected -= OnCharacterRejected;
        }
        _handlersAttached = false;
    }

    private void OnSecureKeyCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not SecureKeyVerifierViewModel vm || sender is not HintedTextBox tb)
        {
            return;
        }

        vm.InsertSecureKeyChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentSecureKeyLength);
    }

    private void OnSecureKeyCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not SecureKeyVerifierViewModel vm || sender is not HintedTextBox tb)
        {
            return;
        }

        vm.RemoveSecureKeyChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentSecureKeyLength);
    }

    private void OnVerifySecureKeyCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not SecureKeyVerifierViewModel vm || sender is not HintedTextBox tb)
        {
            return;
        }

        vm.InsertVerifySecureKeyChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentVerifySecureKeyLength);
    }

    private void OnVerifySecureKeyCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not SecureKeyVerifierViewModel vm || sender is not HintedTextBox tb)
        {
            return;
        }

        vm.RemoveVerifySecureKeyChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentVerifySecureKeyLength);
    }

    private void OnSecureKeyTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        if (DataContext is not SecureKeyVerifierViewModel vm)
        {
            return;
        }

        _ = vm.HandleEnterKeyPressAsync();
        e.Handled = true;
    }

    private void OnCharacterRejected(object? sender, CharacterRejectedEventArgs e)
    {
        if (DataContext is not SecureKeyVerifierViewModel vm || sender is not HintedTextBox tb)
        {
            return;
        }

        string localizedMessage = vm.GetLocalizedWarningMessage(e.WarningType);
        tb.WarningText = localizedMessage;
        tb.HasWarning = true;
    }
}
