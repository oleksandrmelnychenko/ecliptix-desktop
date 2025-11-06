using Ecliptix.Core.Core.Messaging.Services;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Core.Services.Network.Infrastructure;

namespace Ecliptix.Core.Infrastructure.Network.Core.Providers;

public sealed record NetworkProviderServices(
    IConnectivityService ConnectivityService,
    IRetryStrategy RetryStrategy,
    IPendingRequestManager PendingRequestManager);
