using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls;
using Ecliptix.Core.ViewModels.Memberships.SignIn;
using Serilog;

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
        if (_handlersAttached) return;
        if (this.FindControl<HintedTextBox>("SecureKeyTextBox") is { } secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded += OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved += OnSecureKeyCharactersRemoved;
            _handlersAttached = true;
        }
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached) return;
        if (this.FindControl<HintedTextBox>("SecureKeyTextBox") is { } secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded -= OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved -= OnSecureKeyCharactersRemoved;
        }

        _handlersAttached = false;
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
}