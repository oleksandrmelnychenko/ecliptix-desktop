using Ecliptix.Core.Messaging.Controls.Modals;
using Ecliptix.Core.Messaging.Core.Messaging.Events;

namespace Ecliptix.Core.Messaging.Core.Messaging.Services;

public interface IGlobalModalService
{
    Task ShowAsync(ModalLayout layout, object viewModel, bool showScrim = true, bool isDismissable = true);
    Task ShowBottomAsync(object viewModel, bool showScrim = true, bool isDismissable = true);
    Task ShowMiddleAsync(object viewModel, bool showScrim = true, bool isDismissable = true);
    Task ShowRightAsync(object viewModel, bool showScrim = true, bool isDismissable = true);
    Task CloseAllAsync();
    IDisposable OnModalHidden(Func<ModalHiddenEvent, Task> handler, SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK);
}
