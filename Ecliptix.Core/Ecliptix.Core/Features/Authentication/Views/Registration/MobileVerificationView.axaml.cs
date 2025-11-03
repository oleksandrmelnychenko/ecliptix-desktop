using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;
using ReactiveUI;

namespace Ecliptix.Core.Features.Authentication.Views.Registration;

public partial class MobileVerificationView : ReactiveUserControl<MobileVerificationViewModel>
{
    private const string MobileTextBoxControlName = "MobileTextBox";

    private bool _handlersAttached;
    public MobileVerificationView()
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

        if (this.FindControl<HintedTextBox>(MobileTextBoxControlName) is { } mobileTextBox)
        {
            mobileTextBox.KeyDown += OnMobileTextBoxKeyDown;
            _handlersAttached = true;
        }
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached)
        {
            return;
        }

        if (this.FindControl<HintedTextBox>(MobileTextBoxControlName) is { } mobileTextBox)
        {
            mobileTextBox.KeyDown -= OnMobileTextBoxKeyDown;
        }

        _handlersAttached = false;
    }

    private void OnMobileTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
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
                if (task.IsFaulted && task.Exception != null)
                {
                    Serilog.Log.Error(task.Exception, "[MOBILE-VERIFICATION-VIEW] Unhandled exception in HandleEnterKeyPressAsync");
                }
            },
            TaskScheduler.Default);
        e.Handled = true;
    }
}
