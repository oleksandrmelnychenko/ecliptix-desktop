using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;
using Serilog;

namespace Ecliptix.Core.Core.Messaging.Services;

internal sealed class ProfileMenuService : IProfileMenuService, IDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly Lock _stateLock = new();
    private bool _isOpen;
    private bool _isAnimating;
    private bool _disposed;

    public ProfileMenuService(IMessageBus messageBus)
    {
        _messageBus = messageBus;

        _messageBus.Subscribe<ProfileMenuAnimationCompleteEvent>(async evt =>
        {
            await HandleAnimationComplete(evt);
        });
    }

    public async Task ShowAsync()
    {
        if (_disposed)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_isOpen || _isAnimating)
            {
                return;
            }

            _isAnimating = true;
        }

        Log.Information("[PROFILE-MENU-SERVICE] Showing profile menu");
        await _messageBus.PublishAsync(ProfileMenuCommandEvent.Show());
    }

    public async Task HideAsync()
    {
        if (_disposed)
        {
            return;
        }

        lock (_stateLock)
        {
            if (_isAnimating)
            {
                Log.Information("[PROFILE-MENU-SERVICE] Cannot hide - animation in progress");
                return;
            }

            if (!_isOpen)
            {
                Log.Information("[PROFILE-MENU-SERVICE] Cannot hide - menu not open");
                return;
            }

            _isAnimating = true;
        }

        Log.Information("[PROFILE-MENU-SERVICE] Hiding profile menu");
        await _messageBus.PublishAsync(ProfileMenuCommandEvent.Hide());
    }

    public async Task ToggleAsync()
    {
        if (_disposed)
        {
            return;
        }

        bool currentlyOpen = false;

        lock (_stateLock)
        {
            if (_isAnimating)
            {
                return;
            }

            currentlyOpen = _isOpen;
            _isAnimating = true;
        }

        Log.Information("[PROFILE-MENU-SERVICE] Toggling profile menu. Currently open: {IsOpen}", currentlyOpen);
        await _messageBus.PublishAsync(ProfileMenuCommandEvent.Toggle(currentlyOpen));
    }

    private Task HandleAnimationComplete(ProfileMenuAnimationCompleteEvent evt)
    {
        if (_disposed)
        {
            return Task.CompletedTask;
        }

        lock (_stateLock)
        {
            _isAnimating = false;
            _isOpen = evt.AnimationType == ProfileMenuAnimationType.SHOW;
        }

        Log.Information("[PROFILE-MENU-SERVICE] Animation complete. Type: {AnimationType}, IsOpen: {IsOpen}",
            evt.AnimationType, _isOpen);

        return Task.CompletedTask;
    }

    public IDisposable OnProfileMenuChanged(Func<ProfileMenuCommandEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK) =>
        _messageBus.Subscribe(handler, lifetime);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
    }
}
