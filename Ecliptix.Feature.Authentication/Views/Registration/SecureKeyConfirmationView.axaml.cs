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
using Ecliptix.Feature.Authentication.ViewModels.Registration.SecureKeyConfirmation;
using ReactiveUI;

namespace Ecliptix.Feature.Authentication.Views.Registration;

public partial class SecureKeyConfirmationView : ReactiveUserControl<SecureKeyConfirmationViewModel>
{
    private const string SECURE_KEY_TEXT_BOX_CONTROL_NAME = "SecureKeyTextBox";
    private const string VERIFY_SECURE_KEY_TEXT_BOX_CONTROL_NAME = "VerifySecureKeyTextBox";
    private const string ERROR_NOTIFICATION_CONTROL_NAME = "ErrorNotification";
    private bool _handlersAttached;
    private CompositeDisposable? _subscriptions;

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
        SetupErrorNotificationSubscription();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TeardownEventHandlers();
        TeardownErrorNotificationSubscription();
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

    private void SetupEventHandlers()
    {
        if (_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedPasswordBox>(SECURE_KEY_TEXT_BOX_CONTROL_NAME) is { } secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded += OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved += OnSecureKeyCharactersRemoved;
            secureKeyBox.CharacterRejected += OnCharacterRejected;
            secureKeyBox.KeyDown += OnInputControlKeyDown;
        }

        if (this.FindControl<HintedPasswordBox>(VERIFY_SECURE_KEY_TEXT_BOX_CONTROL_NAME) is { } verifySecureKeyBox)
        {
            verifySecureKeyBox.SecureKeyCharactersAdded += OnVerifySecureKeyCharactersAdded;
            verifySecureKeyBox.SecureKeyCharactersRemoved += OnVerifySecureKeyCharactersRemoved;
            verifySecureKeyBox.CharacterRejected += OnCharacterRejected;
            verifySecureKeyBox.KeyDown += OnInputControlKeyDown;
        }

        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedPasswordBox>(SECURE_KEY_TEXT_BOX_CONTROL_NAME) is { } secureKeyBox)
        {
            secureKeyBox.SecureKeyCharactersAdded -= OnSecureKeyCharactersAdded;
            secureKeyBox.SecureKeyCharactersRemoved -= OnSecureKeyCharactersRemoved;
            secureKeyBox.CharacterRejected -= OnCharacterRejected;
            secureKeyBox.KeyDown -= OnInputControlKeyDown;
        }

        if (this.FindControl<HintedPasswordBox>(VERIFY_SECURE_KEY_TEXT_BOX_CONTROL_NAME) is { } verifySecureKeyBox)
        {
            verifySecureKeyBox.SecureKeyCharactersAdded -= OnVerifySecureKeyCharactersAdded;
            verifySecureKeyBox.SecureKeyCharactersRemoved -= OnVerifySecureKeyCharactersRemoved;
            verifySecureKeyBox.CharacterRejected -= OnCharacterRejected;
            verifySecureKeyBox.KeyDown -= OnInputControlKeyDown;
        }

        _handlersAttached = false;
    }

    private void OnSecureKeyCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not SecureKeyConfirmationViewModel vm || sender is not HintedPasswordBox tb)
        {
            return;
        }

        vm.InsertSecureKeyChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentSecureKeyLength);
    }

    private void OnSecureKeyCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not SecureKeyConfirmationViewModel vm || sender is not HintedPasswordBox tb)
        {
            return;
        }

        vm.RemoveSecureKeyChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentSecureKeyLength);
    }

    private void OnVerifySecureKeyCharactersAdded(object? sender, SecureKeyCharactersAddedEventArgs e)
    {
        if (DataContext is not SecureKeyConfirmationViewModel vm || sender is not HintedPasswordBox tb)
        {
            return;
        }

        vm.InsertVerifySecureKeyChars(e.Index, e.Characters);
        tb.SyncSecureKeyState(vm.CurrentVerifySecureKeyLength);
    }

    private void OnVerifySecureKeyCharactersRemoved(object? sender, SecureKeyCharactersRemovedEventArgs e)
    {
        if (DataContext is not SecureKeyConfirmationViewModel vm || sender is not HintedPasswordBox tb)
        {
            return;
        }

        vm.RemoveVerifySecureKeyChars(e.Index, e.Count);
        tb.SyncSecureKeyState(vm.CurrentVerifySecureKeyLength);
    }

    private void OnInputControlKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        if (DataContext is not SecureKeyConfirmationViewModel vm)
        {
            return;
        }

        e.Handled = true;

        vm.HandleEnterKeyPressAsync().ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Serilog.Log.Error(task.Exception,
                        "[SECURE-KEY-CONFIRMATION-VIEW] Unhandled exception in HandleEnterKeyPressAsync");
                }
            },
            TaskScheduler.Default);
    }

    private void OnCharacterRejected(object? sender, CharacterRejectedEventArgs e)
    {
        if (DataContext is not SecureKeyConfirmationViewModel vm || sender is not HintedPasswordBox tb)
        {
            return;
        }

        string localizedMessage = vm.GetLocalizedWarningMessage(e.WarningType);
        tb.WarningText = localizedMessage;
        tb.HasWarning = true;
    }
}
