using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public sealed class SideSheetService: ISideSheetService
{
    private readonly IMessageBus _messageBus;
    private readonly Queue<SideSheetRequest> _requestQueue = new();
    private readonly Lock _queueLock = new();
    private SideSheetRequest? _pendingRequest;
    private bool _isShowingSideSheet;
    private bool _isAnimating;
    private bool _disposed;

    public SideSheetService(IMessageBus messageBus)
    {
        _messageBus = messageBus;
        _messageBus.Subscribe<SideSheetAnimationCompleteEvent>(async evt => await HandleAnimationComplete(evt));
    }

    public async Task ShowAsync(SideSheetComponentType componentType, UserControl? control = null,
        bool showScrim = true, bool isDismissable = true)
    {
        if (_disposed)
        {
            return;
        }

        SideSheetRequest request = new(componentType, control, showScrim, isDismissable);

        lock (_queueLock)
        {
            _requestQueue.Enqueue(request);
        }

        await ProcessNextRequest();
    }

    public async Task HideAsync()
    {
        if (_disposed)
        {
            return;
        }

        lock (_queueLock)
        {
            if (!_isShowingSideSheet || _isAnimating)
            {
                return;
            }

            _isAnimating = true;
        }

        await _messageBus.PublishAsync(SideSheetCommandEvent.Hide());
    }

    public async Task SideSheetDismissed()
    {
        lock (_queueLock)
        {
            if (!_isShowingSideSheet)
            {
                return;
            }
        }

        await _messageBus.PublishAsync(SideSheetCommandEvent.Hide());
    }


    private async Task ProcessNextRequest()
    {
        if (_disposed)
        {
            return;
        }

        SideSheetRequest? requestToShow = null;
        bool shouldHide = false;

        lock (_queueLock)
        {
            if (_isAnimating)
            {
                return;
            }

            if (_requestQueue.Count == 0)
            {
                return;
            }

            if (_isShowingSideSheet)
            {
                _pendingRequest = _requestQueue.Dequeue();
                shouldHide = true;
            }
            else
            {
                requestToShow = _requestQueue.Dequeue();
            }

            _isAnimating = true;
        }

        if (shouldHide)
        {
            await _messageBus.PublishAsync(SideSheetCommandEvent.Hide());
        }
        else
        {
            await _messageBus.PublishAsync(SideSheetCommandEvent.Show(
                requestToShow!.ComponentType,
                requestToShow.Control,
                requestToShow.ShowScrim,
                requestToShow.IsDismissable));
        }
    }

    private async Task HandleAnimationComplete(SideSheetAnimationCompleteEvent evt)
    {
        if (_disposed)
        {
            return;
        }

        SideSheetRequest? requestToShow = null;

        lock (_queueLock)
        {
            _isAnimating = false;

            if (evt.AnimationType == AnimationType.SHOW)
            {
                _isShowingSideSheet = true;
            }
            else // Hide
            {
                _isShowingSideSheet = false;
                if (_pendingRequest != null)
                {
                    requestToShow = _pendingRequest;
                    _pendingRequest = null;
                    _isAnimating = true;
                }
            }
        }

        if (requestToShow != null)
        {
            await _messageBus.PublishAsync(SideSheetCommandEvent.Show(
                requestToShow.ComponentType,
                requestToShow.Control,
                requestToShow.ShowScrim,
                requestToShow.IsDismissable));
        }
        else
        {
            await ProcessNextRequest();
        }
    }

    public IDisposable OnSideSheetChanged(Func<SideSheetCommandEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK) => _messageBus.Subscribe(handler, lifetime);

    public IDisposable OnSideSheetHidden(Func<SideSheetHiddenEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK) => _messageBus.Subscribe(handler, lifetime);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        lock (_queueLock)
        {
            _requestQueue.Clear();
            _pendingRequest = null;
        }
    }

    private sealed record SideSheetRequest(
        SideSheetComponentType ComponentType,
        UserControl? Control,
        bool ShowScrim,
        bool IsDismissable);
}
