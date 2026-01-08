using Ecliptix.Core.Messaging.Core.Messaging.Events;

namespace Ecliptix.Core.Messaging.Core.Messaging.Services;

internal sealed class OverlaySheetService : IOverlaySheetService, IDisposable
{
    private readonly IMessageBus _messageBus;
    private readonly Queue<OverlaySheetRequest> _requestQueue = new();
    private readonly Lock _queueLock = new();
    private OverlaySheetRequest? _pendingRequest;
    private bool _isShowingOverlaySheet;
    private bool _isAnimating;
    private bool _disposed;

    public OverlaySheetService(IMessageBus messageBus)
    {
        _messageBus = messageBus;
        _messageBus.Subscribe<OverlaySheetAnimationCompleteEvent>(async evt =>
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

        OverlaySheetRequest request = new(viewModel, showScrim, isDismissable);

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
            if (!_isShowingOverlaySheet || _isAnimating)
            {
                return;
            }

            _isAnimating = true;
        }

        await _messageBus.PublishAsync(OverlaySheetCommandEvent.Hide());
    }

    public async Task OverlaySheetDismissed()
    {
        lock (_queueLock)
        {
            if (!_isShowingOverlaySheet)
            {
                return;
            }
        }

        await _messageBus.PublishAsync(OverlaySheetCommandEvent.Hide());
    }

    private async Task ProcessNextRequest()
    {
        if (_disposed)
        {
            return;
        }

        OverlaySheetRequest? requestToShow = null;
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

            if (_isShowingOverlaySheet)
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
            await _messageBus.PublishAsync(OverlaySheetCommandEvent.Hide());
        }
        else
        {
            await _messageBus.PublishAsync(OverlaySheetCommandEvent.Show(
                requestToShow!.ViewModel,
                requestToShow.ShowScrim,
                requestToShow.IsDismissable));
        }
    }

    private async Task HandleAnimationComplete(OverlaySheetAnimationCompleteEvent evt)
    {
        if (_disposed)
        {
            return;
        }

        OverlaySheetRequest? requestToShow = null;

        lock (_queueLock)
        {
            _isAnimating = false;

            if (evt.AnimationType == OverlayAnimationType.SHOW)
            {
                _isShowingOverlaySheet = true;
            }
            else 
            {
                _isShowingOverlaySheet = false;
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
            await _messageBus.PublishAsync(OverlaySheetCommandEvent.Show(
                requestToShow.ViewModel,
                requestToShow.ShowScrim,
                requestToShow.IsDismissable));
        }
        else
        {
            await ProcessNextRequest();
        }
    }

    public IDisposable OnOverlaySheetChanged(Func<OverlaySheetCommandEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK) => _messageBus.Subscribe(handler, lifetime);

    public IDisposable OnOverlaySheetHidden(Func<OverlaySheetHiddenEvent, Task> handler,
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

    private sealed record OverlaySheetRequest(
        object ViewModel,
        bool ShowScrim,
        bool IsDismissable);
}
