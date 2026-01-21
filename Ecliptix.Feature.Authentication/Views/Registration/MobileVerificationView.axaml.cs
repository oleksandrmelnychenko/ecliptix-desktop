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
using Ecliptix.Feature.Authentication.ViewModels.Registration.MobileVerification;
using ReactiveUI;

namespace Ecliptix.Feature.Authentication.Views.Registration;

public partial class MobileVerificationView : ReactiveUserControl<MobileVerificationViewModel>
{
    private const string MOBILE_TEXT_BOX_CONTROL_NAME = "MobileTextBox";
    private const string ERROR_NOTIFICATION_CONTROL_NAME = "ErrorNotification";

    private bool _handlersAttached;
    private CompositeDisposable? _subscriptions;
    public MobileVerificationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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

        if (this.FindControl<HintedTextBox>(MOBILE_TEXT_BOX_CONTROL_NAME) is not { } mobileTextBox)
        {
            return;
        }

        mobileTextBox.KeyDown += OnMobileTextBoxKeyDown;
        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedTextBox>(MOBILE_TEXT_BOX_CONTROL_NAME) is { } mobileTextBox)
        {
            mobileTextBox.KeyDown -= OnMobileTextBoxKeyDown;
        }

        _handlersAttached = false;
    }

    private void OnMobileTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is not Key.Enter and not Key.Return)
        {
            return;
        }

        if (DataContext is not MobileVerificationViewModel vm)
        {
            return;
        }

        vm.HandleEnterKeyPressAsync().ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Serilog.Log.Error(task.Exception, "[MOBILE-VERIFICATION-VIEW] Unhandled exception in HandleEnterKeyPressAsync");
                }
            },
            TaskScheduler.Default);
        e.Handled = true;
    }
}
