using Ecliptix.Network.Services.Network.Rpc;

namespace Ecliptix.Network.Services.Abstractions.Network;

public interface IRetryPolicyProvider
{
    RetryBehavior GetRetryBehavior(RpcServiceType serviceType);
}
