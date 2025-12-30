using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Controls.Core.HintedTextControls;
using Ecliptix.Core.Controls.EventArgs;
using Ecliptix.Core.Features.Authentication.ViewModels.SignIn;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.Views.SignIn;

public partial class SignInView : ReactiveUserControl<SignInViewModel>
{
    private const string MOBILE_TEXT_BOX_CONTROL_NAME = "MobileTextBox";
    private const string SECURE_KEY_TEXT_BOX_CONTROL_NAME = "SecureKeyTextBox";
    private const string ERROR_NOTIFICATION_CONTROL_NAME = "ErrorNotification";

    private bool _handlersAttached;
    private CompositeDisposable? _subscriptions;

    public SignInView()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetupEventHandlers();
        SetupErrorNotificationSubscription();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TeardownEventHandlers();
        TeardownErrorNotificationSubscription();
    }

    private void SetupEventHandlers()
    {
        if (_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedTextBox>(MOBILE_TEXT_BOX_CONTROL_NAME) is { } mobileBox)
        {
            mobileBox.KeyDown += OnInputControlKeyDown;
        }

        if (this.FindControl<HintedPasswordBox>(SECURE_KEY_TEXT_BOX_CONTROL_NAME) is { } secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded += OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved += OnSecureKeyCharactersRemoved;
            secureKeyBox.KeyDown += OnInputControlKeyDown;
            secureKeyBox.CharacterRejected += OnCharacterRejected;
            _handlersAttached = true;
        }
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedTextBox>(MOBILE_TEXT_BOX_CONTROL_NAME) is { } mobileBox)
        {
            mobileBox.KeyDown -= OnInputControlKeyDown;
        }

        if (this.FindControl<HintedPasswordBox>(SECURE_KEY_TEXT_BOX_CONTROL_NAME) is { } secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded -= OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved -= OnSecureKeyCharactersRemoved;
            secureKeyBox.KeyDown -= OnInputControlKeyDown;
            secureKeyBox.CharacterRejected -= OnCharacterRejected;
        }

        _handlersAttached = false;
    }

    private void SetupErrorNotificationSubscription()
    {
        if (ViewModel == null)
        {
            return;
        }

        _subscriptions?.Dispose();
        _subscriptions = new CompositeDisposable();

        ViewModel.ExecutionError
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(error =>
            {
                if (this.FindControl<ErrorNotificationView>(ERROR_NOTIFICATION_CONTROL_NAME) is { } notification)
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        notification.ShowError(error);
                    }
                    else
                    {
                        notification.Hide();
                    }
                }
            })
            .DisposeWith(_subscriptions);
    }

    private void TeardownErrorNotificationSubscription()
    {
        _subscriptions?.Dispose();
        _subscriptions = null;
    }

    private void OnCharacterRejected(object? sender, CharacterRejectedEventArgs e)
    {
        if (DataContext is not SignInViewModel vm || sender is not HintedPasswordBox tb)
        {
            return;
        }

        string localizedMessage = vm.GetLocalizedWarningMessage(e.WarningType);
        tb.WarningText = localizedMessage;
        tb.HasWarning = true;
    }

    private void OnSecureKeyCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not SignInViewModel vm || sender is not HintedPasswordBox tb)
        {
            return;
        }

        vm.InsertSecureKeyChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentSecureKeyLength);
    }

    private void OnSecureKeyCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not SignInViewModel vm || sender is not HintedPasswordBox tb)
        {
            return;
        }

        vm.RemoveSecureKeyChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentSecureKeyLength);
    }

    private void OnInputControlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        if (DataContext is not SignInViewModel vm)
        {
            return;
        }

        e.Handled = true;

        vm.HandleEnterKeyPressAsync().ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Serilog.Log.Error(task.Exception, "[SIGNIN-VIEW] Unhandled exception in HandleEnterKeyPressAsync");
                }
            },
            TaskScheduler.Default);
    }
}
