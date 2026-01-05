using Ecliptix.Core.Messaging.Core.Messaging.Events;

namespace Ecliptix.Core.Messaging.Core.Messaging.Services;

public interface IOverlaySheetService
{
    Task ShowAsync(object viewModel, bool showScrim = true, bool isDismissable = true);

    Task HideAsync();

    Task OverlaySheetDismissed();

    IDisposable OnOverlaySheetHidden(
        Func<OverlaySheetHiddenEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK);
}
