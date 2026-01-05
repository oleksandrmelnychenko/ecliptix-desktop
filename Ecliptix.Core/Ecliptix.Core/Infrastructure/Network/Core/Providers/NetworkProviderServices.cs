using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Infrastructure;

namespace Ecliptix.Network.Network.Core.Providers;

public sealed record NetworkProviderServices(
    IConnectivityService ConnectivityService,
    IRetryStrategy RetryStrategy,
    IPendingRequestManager PendingRequestManager);
