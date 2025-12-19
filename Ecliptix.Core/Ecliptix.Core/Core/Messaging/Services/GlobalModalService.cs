using System;
using System.Threading.Tasks;
using Ecliptix.Core.Controls.Modals;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public sealed class GlobalModalService : IGlobalModalService
{
    private readonly IMessageBus _messageBus;
    private readonly IBottomSheetService _bottomSheetService;
    private readonly IOverlaySheetService _overlaySheetService;
    private readonly ISideSheetService _sideSheetService;

    public GlobalModalService(IMessageBus messageBus)
    {
        _messageBus = messageBus;
        _bottomSheetService = new BottomSheetService(messageBus);
        _overlaySheetService = new OverlaySheetService(messageBus);
        _sideSheetService = new SideSheetService(messageBus);

        _bottomSheetService.OnBottomSheetHidden(_ =>
        {
            messageBus.PublishAsync(new ModalHiddenEvent(ModalLayout.Bottom));
            return Task.CompletedTask;
        });

        _overlaySheetService.OnOverlaySheetHidden(_ =>
        {
            messageBus.PublishAsync(new ModalHiddenEvent(ModalLayout.Center));
            return Task.CompletedTask;
        });

        _sideSheetService.OnSideSheetHidden(_ =>
        {
            messageBus.PublishAsync(new ModalHiddenEvent(ModalLayout.Right));
            return Task.CompletedTask;
        });
    }

    public async Task ShowAsync(ModalLayout layout, object viewModel, bool showScrim = true, bool isDismissable = true)
    {
        // Close all other modals before opening a new one
        await CloseAllAsync();

        switch (layout)
        {
            case ModalLayout.Bottom:
                await _bottomSheetService.ShowAsync(viewModel, showScrim, isDismissable);
                break;
            case ModalLayout.Center:
                await _overlaySheetService.ShowAsync(viewModel, showScrim, isDismissable);
                break;
            case ModalLayout.Right:
                await _sideSheetService.ShowAsync(viewModel, showScrim, isDismissable);
                break;
            case ModalLayout.Left:
                // Currently no dedicated Left Side Sheet service/method.
                // If needed, implement ISideSheetService.ShowLeftAsync and call it here.
                break;
        }
    }

    public Task ShowBottomAsync(object viewModel, bool showScrim = true, bool isDismissable = true) =>
        ShowAsync(ModalLayout.Bottom, viewModel, showScrim, isDismissable);

    public Task ShowMiddleAsync(object viewModel, bool showScrim = true, bool isDismissable = true) =>
        ShowAsync(ModalLayout.Center, viewModel, showScrim, isDismissable);

    public Task ShowRightAsync(object viewModel, bool showScrim = true, bool isDismissable = true) =>
        ShowAsync(ModalLayout.Right, viewModel, showScrim, isDismissable);

    public async Task CloseAllAsync()
    {
        await _bottomSheetService.HideAsync();
        await _overlaySheetService.HideAsync();
        await _sideSheetService.HideAsync();
    }

    public IDisposable OnModalHidden(Func<ModalHiddenEvent, Task> handler, SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK)
    {
        return _messageBus.Subscribe(handler, lifetime);
    }
}
