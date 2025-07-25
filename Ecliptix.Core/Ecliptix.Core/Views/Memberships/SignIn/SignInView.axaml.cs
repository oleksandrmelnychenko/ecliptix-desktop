// Ecliptix.Core.Views.Memberships.SignIn.SignInView.axaml.cs

using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls;
using Ecliptix.Core.ViewModels.Memberships.SignIn;

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
        if (this.FindControl<HintedTextBox>("PasswordTextBox") is { } passwordBox)
        {
            passwordBox.PasswordCharactersAdded += OnPasswordCharactersAdded;
            passwordBox.PasswordCharactersRemoved += OnPasswordCharactersRemoved;
            _handlersAttached = true;
        }
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached) return;
        if (this.FindControl<HintedTextBox>("PasswordTextBox") is { } passwordBox)
        {
            passwordBox.PasswordCharactersAdded -= OnPasswordCharactersAdded;
            passwordBox.PasswordCharactersRemoved -= OnPasswordCharactersRemoved;
        }
        _handlersAttached = false;
    }

    private void OnPasswordCharactersAdded(object? sender, PasswordCharactersAddedEventArgs e)
    {
        if (DataContext is not SignInViewModel vm || sender is not HintedTextBox tb) return;
        
        vm.InsertPasswordChars(e.Index, e.Characters);
        tb.SyncPasswordState(vm.CurrentPasswordLength);
    }

    private void OnPasswordCharactersRemoved(object? sender, PasswordCharactersRemovedEventArgs e)
    {
        if (DataContext is not SignInViewModel vm || sender is not HintedTextBox tb) return;

        vm.RemovePasswordChars(e.Index, e.Count);
        tb.SyncPasswordState(vm.CurrentPasswordLength);
    }
}