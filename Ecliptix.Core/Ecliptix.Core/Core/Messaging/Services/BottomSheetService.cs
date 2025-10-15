using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

internal sealed class BottomSheetService(IMessageBus messageBus) : IBottomSheetService, IDisposable
{
    private bool _disposed;

    public async Task ShowAsync(BottomSheetComponentType componentType, UserControl? control = null, bool showScrim = true, bool isDismissable = true)
    {
        if (_disposed) return;

        BottomSheetChangedEvent evt = BottomSheetChangedEvent.New(componentType, showScrim, control, isDismissable);
        await messageBus.PublishAsync(evt);
    }

    public async Task HideAsync()
    {
        if (_disposed) return;

        BottomSheetChangedEvent evt = BottomSheetChangedEvent.New(BottomSheetComponentType.Hidden, false);
        await messageBus.PublishAsync(evt);
    }

    public IDisposable OnBottomSheetChanged(
        Func<BottomSheetChangedEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak)
    {
        return messageBus.Subscribe(handler, lifetime);
    }

    public IDisposable OnBottomSheetHidden(
        Func<BottomSheetHiddenEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak)
    {
        return messageBus.Subscribe(handler, lifetime);
    }

    public async Task BottomSheetDismissed()
    {
        await messageBus.PublishAsync(BottomSheetHiddenEvent.UserDismissed());
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
