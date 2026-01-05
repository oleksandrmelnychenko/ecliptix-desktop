using System;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;

namespace Ecliptix.Core.Controls.Core;

public sealed partial class ConnectivityNotificationView : ReactiveUserControl<ConnectivityNotificationViewModel>
{
    public ConnectivityNotificationView()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is not ConnectivityNotificationViewModel viewModel)
        {
            return;
        }

        if (this.TryGetResource("RestoredStateDuration", null, out object? resource) &&
            resource is TimeSpan duration)
        {
            viewModel.RestoredStateDuration = duration;
        }
    }
}
