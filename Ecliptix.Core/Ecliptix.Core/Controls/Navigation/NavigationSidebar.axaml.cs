using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Core.Messaging.Services;
using Serilog;
using Splat;

namespace Ecliptix.Core.Controls.Navigation;

public partial class NavigationSidebar : UserControl, IDisposable
{
    private readonly IProfileMenuService? _profileMenuService;
    private readonly IMessageBus? _messageBus;
    private readonly CompositeDisposable _disposables = new();
    private Canvas? _profileMenuCanvas;
    private Border? _profileMenuContainer;
    private Button? _logoutButton;
    private bool _disposed;

    public NavigationSidebar()
    {
        AvaloniaXamlLoader.Load(this);

        try
        {
            _profileMenuService = Locator.Current?.GetService<IProfileMenuService>();
            _messageBus = Locator.Current?.GetService<IMessageBus>();

            if (_messageBus != null)
            {
                Log.Information("[NAVIGATION-SIDEBAR] Subscribing to ProfileMenuCommandEvent");
                _messageBus.Subscribe<ProfileMenuCommandEvent>(async evt =>
                {
                    await HandleProfileMenuEvent(evt);
                }, SubscriptionLifetime.STRONG).DisposeWith(_disposables);
            }
            else
            {
                Log.Warning("[NAVIGATION-SIDEBAR] MessageBus not available");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NAVIGATION-SIDEBAR] Error initializing profile menu service");
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        _profileMenuCanvas = this.FindControl<Canvas>("ProfileMenuCanvas");
        _profileMenuContainer = this.FindControl<Border>("ProfileMenuContainer");
        _logoutButton = this.FindControl<Button>("LogoutButton");

        if (_logoutButton != null)
        {
            _logoutButton.Click += OnLogoutClicked;
        }
    }

    private async Task HandleProfileMenuEvent(ProfileMenuCommandEvent evt)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            bool isVisible = evt.AnimationType == ProfileMenuAnimationType.SHOW;

            Log.Information("[NAVIGATION-SIDEBAR] Profile menu event: {AnimationType}, setting IsVisible={IsVisible}",
                evt.AnimationType, isVisible);

            if (isVisible)
            {
                if (_profileMenuCanvas != null)
                {
                    _profileMenuCanvas.IsVisible = true;
                }

                if (_profileMenuContainer != null)
                {
                    _profileMenuContainer.Opacity = 1.0;
                    Avalonia.Media.TranslateTransform transform = new Avalonia.Media.TranslateTransform
                    {
                        Y = 0.0
                    };
                    _profileMenuContainer.RenderTransform = transform;
                }
            }
            else
            {
                if (_profileMenuContainer != null)
                {
                    _profileMenuContainer.Opacity = 0.0;
                    Avalonia.Media.TranslateTransform transform = new Avalonia.Media.TranslateTransform
                    {
                        Y = 10.0
                    };
                    _profileMenuContainer.RenderTransform = transform;
                }

                await Task.Delay(150);

                if (_profileMenuCanvas != null)
                {
                    _profileMenuCanvas.IsVisible = false;
                }
            }

            ProfileMenuAnimationCompleteEvent completeEvent = isVisible
                ? ProfileMenuAnimationCompleteEvent.ShowComplete()
                : ProfileMenuAnimationCompleteEvent.HideComplete();

            await _messageBus!.PublishAsync(completeEvent);

            Log.Information("[NAVIGATION-SIDEBAR] Animation complete event published: {AnimationType}", evt.AnimationType);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[NAVIGATION-SIDEBAR] Error handling profile menu event");
        }
    }

    private async void OnLogoutClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_profileMenuService != null)
        {
            await _profileMenuService.HideAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_logoutButton != null)
        {
            _logoutButton.Click -= OnLogoutClicked;
        }

        _disposables.Dispose();
    }
}
