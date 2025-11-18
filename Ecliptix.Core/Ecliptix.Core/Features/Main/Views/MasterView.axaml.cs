using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Features.Main.ViewModels;
using Serilog;
using Splat;

namespace Ecliptix.Core.Features.Main.Views;

public partial class MasterView : UserControl, IDisposable
{
    private readonly IProfileMenuService? _profileMenuService;
    private readonly IMessageBus? _messageBus;
    private readonly CompositeDisposable _disposables = new();
    private Border? _overlayBorder;
    private bool _disposed;

    public MasterView()
    {
        AvaloniaXamlLoader.Load(this);

        try
        {
            _profileMenuService = Locator.Current?.GetService<IProfileMenuService>();
            _messageBus = Locator.Current?.GetService<IMessageBus>();

            if (_messageBus != null)
            {
                Log.Information("[MASTER-VIEW] Subscribing to ProfileMenuCommandEvent");
                _messageBus.Subscribe<ProfileMenuCommandEvent>(async evt =>
                {
                    await HandleProfileMenuEvent(evt);
                }, SubscriptionLifetime.STRONG).DisposeWith(_disposables);
            }
            else
            {
                Log.Warning("[MASTER-VIEW] MessageBus not available");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MASTER-VIEW] Error initializing profile menu service");
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _overlayBorder = this.FindControl<Border>("ProfileMenuOverlay");
    }

    private async System.Threading.Tasks.Task HandleProfileMenuEvent(ProfileMenuCommandEvent evt)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            bool isVisible = evt.AnimationType == ProfileMenuAnimationType.SHOW;

            Log.Information("[MASTER-VIEW] Profile menu event: {AnimationType}, setting overlay IsVisible={IsVisible}",
                evt.AnimationType, isVisible);

            if (_overlayBorder != null)
            {
                _overlayBorder.IsVisible = isVisible;
                _overlayBorder.Opacity = isVisible ? 1.0 : 0.0;
            }

            await System.Threading.Tasks.Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[MASTER-VIEW] Error handling profile menu event");
        }
    }

    private async void OnOverlayClicked(object? sender, PointerPressedEventArgs e)
    {
        Log.Information("[MASTER-VIEW] Overlay clicked");
        if (_profileMenuService != null)
        {
            await _profileMenuService.HideAsync();
        }
        else
        {
            Log.Warning("[MASTER-VIEW] ProfileMenuService is null, cannot hide");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _disposables.Dispose();
    }
}
