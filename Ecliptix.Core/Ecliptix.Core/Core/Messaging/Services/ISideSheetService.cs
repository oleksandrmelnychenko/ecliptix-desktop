using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public interface ISideSheetService
{
    Task ShowAsync(SideSheetComponentType componentType, UserControl? control = null, bool showScrim = true, bool isDismissable = true);
    Task HideAsync();
    Task SideSheetDismissed();
    IDisposable OnSideSheetHidden(Func<SideSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK);
}
