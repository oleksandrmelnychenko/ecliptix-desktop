using System;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging.Events;

namespace Ecliptix.Core.Core.Messaging.Services;

public interface ILanguageDetectionService
{
    Task RequestLanguageChangeAsync(string targetCulture);

    Task ConfirmLanguageChangeAsync(string targetCulture);

    Task DeclineLanguageChangeAsync();

    IDisposable OnLanguageDetectionRequested(
        Func<LanguageDetectionDialogEvent, Task> handler,
        SubscriptionLifetime lifetime = SubscriptionLifetime.Weak);
}
