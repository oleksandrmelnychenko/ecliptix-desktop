using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

internal sealed class LanguageDetectionService(IMessageBus messageBus) : ILanguageDetectionService, IDisposable
{
    private bool _disposed;

    public async Task RequestLanguageChangeAsync(string targetCulture)
    {
        if (_disposed)
        {
            return;
        }

        LanguageDetectionDialogEvent evt = LanguageDetectionDialogEvent.Request(targetCulture);
        await messageBus.PublishAsync(evt);
    }

    public async Task ConfirmLanguageChangeAsync(string targetCulture)
    {
        if (_disposed)
        {
            return;
        }

        LanguageDetectionDialogEvent evt = LanguageDetectionDialogEvent.Confirm(targetCulture);
        await messageBus.PublishAsync(evt);
    }

    public async Task DeclineLanguageChangeAsync()
    {
        if (_disposed)
        {
            return;
        }

        LanguageDetectionDialogEvent evt = LanguageDetectionDialogEvent.Decline();
        await messageBus.PublishAsync(evt);
    }

    public IDisposable OnLanguageDetectionRequested(
        Func<LanguageDetectionDialogEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak)
    {
        return messageBus.Subscribe(handler, lifetime);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
