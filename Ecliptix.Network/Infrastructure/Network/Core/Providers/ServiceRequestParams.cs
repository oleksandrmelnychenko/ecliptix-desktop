using Ecliptix.Network.Services.Network.Rpc;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

internal readonly record struct ServiceRequestParams(
    uint ConnectId,
    RpcServiceType ServiceType,
    byte[] PlainBuffer,
    ServiceFlowType FlowType,
    Func<byte[], Task<Result<Unit, NetworkFailure>>> OnCompleted,
    RpcRequestContext? RequestContext,
    bool AllowDuplicateRequests,
    bool WaitForRecovery,
    CancellationToken CancellationToken);
