using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

internal sealed class BottomSheetService : IBottomSheetService, IDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly Queue<BottomSheetRequest> _requestQueue = new();
    private readonly Lock _queueLock = new();
    private BottomSheetRequest? _pendingRequest;
    private bool _isShowingBottomSheet;
    private bool _isAnimating;
    private bool _disposed;

    public BottomSheetService(IMessageBus messageBus)
    {
        _messageBus = messageBus;
        _messageBus.Subscribe<BottomSheetAnimationCompleteEvent>(async evt =>
        {
            await HandleAnimationComplete(evt);
        });
    }

    public async Task ShowAsync(object viewModel, bool showScrim = true, bool isDismissable = true)
    {
        if (_disposed)
        {
            return;
        }

        BottomSheetRequest request = new(viewModel, showScrim, isDismissable);

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
            if (!_isShowingBottomSheet || _isAnimating)
            {
                return;
            }

            _isAnimating = true;
        }

        await _messageBus.PublishAsync(BottomSheetCommandEvent.Hide());
    }

    public async Task BottomSheetDismissed()
    {
        lock (_queueLock)
        {
            if (!_isShowingBottomSheet)
            {
                return;
            }
        }

        await _messageBus.PublishAsync(BottomSheetCommandEvent.Hide());
    }

    private async Task ProcessNextRequest()
    {
        if (_disposed)
        {
            return;
        }

        BottomSheetRequest? requestToShow = null;
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

            if (_isShowingBottomSheet)
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
            await _messageBus.PublishAsync(BottomSheetCommandEvent.Hide());
        }
        else
        {
            await _messageBus.PublishAsync(BottomSheetCommandEvent.Show(
                requestToShow!.ViewModel,
                requestToShow.ShowScrim,
                requestToShow.IsDismissable));
        }
    }

    private async Task HandleAnimationComplete(BottomSheetAnimationCompleteEvent evt)
    {
        if (_disposed)
        {
            return;
        }

        BottomSheetRequest? requestToShow = null;

        lock (_queueLock)
        {
            _isAnimating = false;

            if (evt.AnimationType == AnimationType.SHOW)
            {
                _isShowingBottomSheet = true;
            }
            else // Hide
            {
                _isShowingBottomSheet = false;
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
            await _messageBus.PublishAsync(BottomSheetCommandEvent.Show(
                requestToShow.ViewModel,
                requestToShow.ShowScrim,
                requestToShow.IsDismissable));
        }
        else
        {
            await ProcessNextRequest();
        }
    }

    public IDisposable OnBottomSheetChanged(Func<BottomSheetCommandEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK) => _messageBus.Subscribe(handler, lifetime);

    public IDisposable OnBottomSheetHidden(Func<BottomSheetHiddenEvent, Task> handler,
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

    private sealed record BottomSheetRequest(
        object ViewModel,
        bool ShowScrim,
        bool IsDismissable);
}
