using Ecliptix.Core.Messaging.Core.Messaging.Events;

namespace Ecliptix.Core.Messaging.Core.Messaging.Services;

public interface IProfileMenuService
{
    Task ShowAsync();
    Task HideAsync();
    Task ToggleAsync();
    IDisposable OnProfileMenuChanged(Func<ProfileMenuCommandEvent, Task> handler, SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK);
}
