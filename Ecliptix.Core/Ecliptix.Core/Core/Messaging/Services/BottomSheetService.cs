using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Messaging.Events;
using Serilog;

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
        Log.Debug("[BottomSheet-Service] Service created");

        _messageBus.Subscribe<BottomSheetAnimationCompleteEvent>(async evt =>
        {
            Log.Debug("[BottomSheet-Service] Animation complete: Type={Type}", evt.AnimationType);
            await HandleAnimationComplete(evt);
        });
    }

    public async Task ShowAsync(BottomSheetComponentType componentType, UserControl? control = null, bool showScrim = true, bool isDismissable = true)
    {
        if (_disposed)
        {
            Log.Warning("[BottomSheet-Service] ShowAsync called on disposed service");
            return;
        }

        Log.Debug("[BottomSheet-Service] ShowAsync requested: Type={Type}", componentType);

        BottomSheetRequest request = new(componentType, control, showScrim, isDismissable);

        lock (_queueLock)
        {
            _requestQueue.Enqueue(request);
            Log.Debug("[BottomSheet-Service] Request queued. Queue size: {QueueSize}", _requestQueue.Count);
        }

        await ProcessNextRequest();
    }


    public async Task HideAsync()
    {
        if (_disposed)
        {
            Log.Warning("[BottomSheet-Service] HideAsync called on disposed service");
            return;
        }

        Log.Debug("[BottomSheet-Service] HideAsync requested");

        lock (_queueLock)
        {
            if (!_isShowingBottomSheet || _isAnimating)
            {
                Log.Debug("[BottomSheet-Service] Cannot hide - not showing or already animating");
                return;
            }

            _isAnimating = true;
        }

        await _messageBus.PublishAsync(BottomSheetCommandEvent.Hide());
    }

    public async Task BottomSheetDismissed()
    {
        Log.Debug("[BottomSheet-Service] BottomSheetDismissed by user");

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
                Log.Debug("[BottomSheet-Service] Cannot process: Already animating");
                return;
            }

            if (_requestQueue.Count == 0)
            {
                Log.Debug("[BottomSheet-Service] Queue empty, nothing to process");
                return;
            }

            if (_isShowingBottomSheet)
            {
                _pendingRequest = _requestQueue.Dequeue();
                Log.Debug("[BottomSheet-Service] Sheet showing, hiding current to show next. Type={Type}, Queue remaining: {QueueSize}",
                    _pendingRequest.ComponentType, _requestQueue.Count);
                shouldHide = true;
                _isAnimating = true;
            }
            else
            {
                requestToShow = _requestQueue.Dequeue();
                _isAnimating = true;
                Log.Debug("[BottomSheet-Service] Processing request: Type={Type}, Queue remaining: {QueueSize}",
                    requestToShow.ComponentType, _requestQueue.Count);
            }
        }

        if (shouldHide)
        {
            Log.Debug("[BottomSheet-Service] Waiting for show animation to fully complete before hiding...");
            await _messageBus.PublishAsync(BottomSheetCommandEvent.Hide());
        }
        else
        {
            await _messageBus.PublishAsync(BottomSheetCommandEvent.Show(
                requestToShow!.ComponentType,
                requestToShow.Control,
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

            if (evt.AnimationType == AnimationType.Show)
            {
                _isShowingBottomSheet = true;
                Log.Debug("[BottomSheet-Service] Show animation complete - sheet is now visible. Queue has {QueueCount} items", _requestQueue.Count);
            }
            else // Hide
            {
                _isShowingBottomSheet = false;
                Log.Debug("[BottomSheet-Service] Hide animation complete - sheet is now hidden");

                if (_pendingRequest != null)
                {
                    requestToShow = _pendingRequest;
                    _pendingRequest = null;
                    _isAnimating = true;
                    Log.Debug("[BottomSheet-Service] Showing pending request: Type={Type}", requestToShow.ComponentType);
                }
            }
        }

        if (requestToShow != null)
        {
            await _messageBus.PublishAsync(BottomSheetCommandEvent.Show(
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

    public IDisposable OnBottomSheetChanged(Func<BottomSheetCommandEvent, Task> handler, SubscriptionLifetime lifetime = SubscriptionLifetime.Weak)
    {
        return _messageBus.Subscribe(handler, lifetime);
    }

    public IDisposable OnBottomSheetHidden(Func<BottomSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime = SubscriptionLifetime.Weak)
    {
        return _messageBus.Subscribe(handler, lifetime);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Log.Debug("[BottomSheet-Service] Service disposing");
        _disposed = true;

        lock (_queueLock)
        {
            _requestQueue.Clear();
            _pendingRequest = null;
        }
    }

    private sealed record BottomSheetRequest(
        BottomSheetComponentType ComponentType,
        UserControl? Control,
        bool ShowScrim,
        bool IsDismissable);
}
