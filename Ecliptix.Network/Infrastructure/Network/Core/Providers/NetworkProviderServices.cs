using Ecliptix.Core.Messaging.Core.Messaging.Services;
using Ecliptix.Network.Services.Abstractions.Network;
using Ecliptix.Network.Services.Network.Infrastructure;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

public sealed record NetworkProviderServices(
    IConnectivityService ConnectivityService,
    IRetryStrategy RetryStrategy,
    IPendingRequestManager PendingRequestManager);
