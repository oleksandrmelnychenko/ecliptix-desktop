using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public interface IProfileMenuService
{
    Task ShowAsync();
    Task HideAsync();
    Task ToggleAsync();
    IDisposable OnProfileMenuChanged(Func<ProfileMenuCommandEvent, Task> handler, SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK);
}
