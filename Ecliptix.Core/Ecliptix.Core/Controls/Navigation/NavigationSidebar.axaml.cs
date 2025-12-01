using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.ViewModels.Navigation;

namespace Ecliptix.Core.Controls.Navigation;

public partial class NavigationSidebar : ReactiveUserControl<NavigationSidebarViewModel>
{
    private Button? _createFlyoutButton;

    private CompositeDisposable? _visualTreeDisposables;

    public NavigationSidebar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void HideFlyout()
    {
        _createFlyoutButton?.Flyout?.Hide();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _createFlyoutButton = this.FindControl<Button>("CreateFlyoutButton");

        _visualTreeDisposables = new CompositeDisposable();

    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _visualTreeDisposables?.Dispose();
        _visualTreeDisposables = null;

        base.OnDetachedFromVisualTree(e);
    }
}
