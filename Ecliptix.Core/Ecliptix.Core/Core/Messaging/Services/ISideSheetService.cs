using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public interface ISideSheetService
{
    Task ShowAsync(object viewModel, bool showScrim = true, bool isDismissable = true);
    Task HideAsync();
    Task SideSheetDismissed();
    IDisposable OnSideSheetHidden(Func<SideSheetHiddenEvent, Task> handler, SubscriptionLifetime lifetime = SubscriptionLifetime.WEAK);
}
