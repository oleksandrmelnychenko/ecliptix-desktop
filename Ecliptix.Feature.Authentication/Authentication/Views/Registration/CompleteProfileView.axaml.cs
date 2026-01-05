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
using Ecliptix.Feature.Authentication.Authentication.ViewModels.Registration;
using ReactiveUI;

namespace Ecliptix.Feature.Authentication.Authentication.Views.Registration;

public partial class CompleteProfileView : ReactiveUserControl<CompleteProfileViewModel>
{
    private const string PROFILE_NAME_TEXTBOX = "ProfileNameTextBox";
    private const string DISPLAY_NAME_TEXTBOX = "DisplayNameTextBox";
    private const string ERROR_NOTIFICATION_CONTROL_NAME = "ErrorNotification";

    private bool _handlersAttached;
    private CompositeDisposable? _subscriptions;

    public CompleteProfileView()
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

        if (this.FindControl<HintedTextBox>(PROFILE_NAME_TEXTBOX) is { } profileBox)
        {
            profileBox.KeyDown += OnTextBoxKeyDown;
        }

        if (this.FindControl<HintedTextBox>(DISPLAY_NAME_TEXTBOX) is { } displayBox)
        {
            displayBox.KeyDown += OnTextBoxKeyDown;
        }

        _handlersAttached = true;
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedTextBox>(PROFILE_NAME_TEXTBOX) is { } profileBox)
        {
            profileBox.KeyDown -= OnTextBoxKeyDown;
        }

        if (this.FindControl<HintedTextBox>(DISPLAY_NAME_TEXTBOX) is { } displayBox)
        {
            displayBox.KeyDown -= OnTextBoxKeyDown;
        }

        _handlersAttached = false;
    }

    private void OnTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        if (DataContext is not CompleteProfileViewModel vm)
        {
            return;
        }

        e.Handled = true;

        vm.HandleEnterKeyPressAsync().ContinueWith(
            task =>
            {
                if (task is { IsFaulted: true, Exception: not null })
                {
                    Serilog.Log.Error(task.Exception, "[COMPLETE-PROFILE-VIEW] Unhandled exception in HandleEnterKeyPressAsync");
                }
            },
            TaskScheduler.Default);
    }
}

