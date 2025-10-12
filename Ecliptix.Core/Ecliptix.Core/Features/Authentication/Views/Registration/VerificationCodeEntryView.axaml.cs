using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Controls.Core;
using Ecliptix.Core.Features.Authentication.ViewModels.Registration;

namespace Ecliptix.Core.Features.Authentication.Views.Registration;

public partial class VerificationCodeEntryView : ReactiveUserControl<VerifyOtpViewModel>
{
    private bool _handlersAttached;

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
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        TeardownEventHandlers();
    }

    private void SetupEventHandlers()
    {
        if (_handlersAttached) return;
        if (this.FindControl<SegmentedTextBox>("SegmentedTextBox") is { } segmentedTextBox)
        {
            segmentedTextBox.KeyDown += OnSegmentedTextBoxKeyDown;
            _handlersAttached = true;
        }
    }

    private void TeardownEventHandlers()
    {
        if (!_handlersAttached) return;
        if (this.FindControl<SegmentedTextBox>("SegmentedTextBox") is { } segmentedTextBox)
        {
            segmentedTextBox.KeyDown -= OnSegmentedTextBoxKeyDown;
        }
        _handlersAttached = false;
    }

    private void OnSegmentedTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;

        if (DataContext is not VerifyOtpViewModel vm) return;

        _ = vm.HandleEnterKeyPressAsync();
        e.Handled = true;
    }
}