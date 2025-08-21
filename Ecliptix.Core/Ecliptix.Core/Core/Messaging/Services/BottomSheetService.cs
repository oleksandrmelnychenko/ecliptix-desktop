using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public sealed class BottomSheetService(IUnifiedMessageBus messageBus) : IBottomSheetService, IDisposable
{
    private bool _disposed;

    public async Task ShowAsync(BottomSheetComponentType componentType, UserControl? control = null, bool showScrim = true)
    {
        if (_disposed) return;
        
        BottomSheetChangedEvent evt = BottomSheetChangedEvent.New(componentType, showScrim, control);
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

    public void Dispose()
    {
        _disposed = true;
    }
}