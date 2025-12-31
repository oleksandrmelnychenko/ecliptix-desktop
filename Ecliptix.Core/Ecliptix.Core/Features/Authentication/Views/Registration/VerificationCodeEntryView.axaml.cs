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
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.Views.Registration;

public sealed partial class VerificationCodeEntryView : ReactiveUserControl<VerificationCodeEntryViewModel>
{
    private const string ERROR_NOTIFICATION_CONTROL_NAME = "ErrorNotification";
    private const string OTP_TEXT_BOX_CONTROL_NAME = "OtpTextBox";
    private bool _handlersAttached;
    private CompositeDisposable? _subscriptions;

    public VerificationCodeEntryView()
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

    private void SetupEventHandlers()
    {
        if (_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedTextBox>(OTP_TEXT_BOX_CONTROL_NAME) is { } otpTextBox)
        {
            otpTextBox.KeyDown += OnOtpTextBoxKeyDown;
            _handlersAttached = true;
        }
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

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedTextBox>(OTP_TEXT_BOX_CONTROL_NAME) is { } otpTextBox)
        {
            otpTextBox.KeyDown -= OnOtpTextBoxKeyDown;
        }

        _handlersAttached = false;
    }

    private void OnOtpTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        if (DataContext is not VerificationCodeEntryViewModel vm)
        {
            return;
        }

        vm.HandleEnterKeyPressAsync().ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Serilog.Log.Error(task.Exception,
                        "[VERIFICATION-CODE-ENTRY-VIEW] Unhandled exception in HandleEnterKeyPressAsync");
                }
            },
            TaskScheduler.Default);
        e.Handled = true;
    }
}
