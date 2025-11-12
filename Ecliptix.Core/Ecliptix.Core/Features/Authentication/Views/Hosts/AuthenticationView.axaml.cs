using System;
using Avalonia;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Ecliptix.Core.Features.Authentication.ViewModels.Hosts;
using Serilog;

namespace Ecliptix.Core.Features.Authentication.Views.Hosts;

public partial class AuthenticationView : ReactiveUserControl<AuthenticationViewModel>
{
    public AuthenticationView()
    {
        Log.Information("[AUTH-VIEW] Constructor called");
        AvaloniaXamlLoader.Load(this);
        Log.Information("[AUTH-VIEW] XAML loaded");
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Log.Information("[AUTH-VIEW] DataContext changed to: {Type}", DataContext?.GetType().Name ?? "null");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Log.Information("[AUTH-VIEW] ✅ Attached to visual tree");
    }
}
