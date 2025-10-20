using Ecliptix.Core.Services.Network.Rpc;

namespace Ecliptix.Core.Services.Abstractions.Network;

public interface IRetryPolicyProvider
{
    RetryBehavior GetRetryBehavior(RpcServiceType serviceType);
}
