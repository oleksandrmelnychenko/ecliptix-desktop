using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public interface IBottomSheetService
{
    Task ShowAsync(BottomSheetComponentType componentType, UserControl? control = null, bool showScrim = true, bool isDismissable = true);

    Task HideAsync();

    IDisposable OnBottomSheetChanged(
        Func<BottomSheetChangedEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak);
}