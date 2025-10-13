using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.Common;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.EventArgs;
using Ecliptix.Core.Features.Authentication.ViewModels.SignIn;

namespace Ecliptix.Core.Features.Authentication.Views.SignIn;

public partial class SignInView : ReactiveUserControl<SignInViewModel>
{
    private const string SecureKeyTextBoxControlName = "SecureKeyTextBox";

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
        if (_handlersAttached) return;
        if (this.FindControl<HintedTextBox>(SecureKeyTextBoxControlName) is { } secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded += OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved += OnSecureKeyCharactersRemoved;
            secureKeyBox.KeyDown += OnSecureKeyBoxKeyDown;
            secureKeyBox.CharacterRejected += OnCharacterRejected;
            _handlersAttached = true;
        }
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached) return;
        if (this.FindControl<HintedTextBox>(SecureKeyTextBoxControlName) is { } secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded -= OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved -= OnSecureKeyCharactersRemoved;
            secureKeyBox.KeyDown -= OnSecureKeyBoxKeyDown;
            secureKeyBox.CharacterRejected -= OnCharacterRejected;
        }

        _handlersAttached = false;
    }

    private void OnCharacterRejected(object? sender, CharacterRejectedEventArgs e)
    {
        if (DataContext is not SignInViewModel vm || sender is not HintedTextBox tb) return;

        string localizedMessage = vm.GetLocalizedWarningMessage(e.WarningType);
        tb.WarningText = localizedMessage;
        tb.HasWarning = true;
    }


    private void OnSecureKeyCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not SignInViewModel vm || sender is not HintedTextBox tb) return;
        vm.InsertSecureKeyChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentSecureKeyLength);
    }

    private void OnSecureKeyCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not SignInViewModel vm || sender is not HintedTextBox tb) return;
        vm.RemoveSecureKeyChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentSecureKeyLength);
    }

    private void OnSecureKeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;

        if (DataContext is not SignInViewModel vm) return;

        _ = vm.HandleEnterKeyPressAsync();
        e.Handled = true;
    }
}
