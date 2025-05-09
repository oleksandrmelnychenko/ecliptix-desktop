using System.Threading.Tasks;
using Ecliptix.Core.Protobuf.VerificationServices;
using Ecliptix.Core.Protocol.Utilities;

namespace Ecliptix.Core.Network;

public class ReceiveStreamExecutor(
    VerificationServiceActions.VerificationServiceActionsClient verificationServiceActionsClient)
{
    public async Task<Result<RpcFlow, ShieldFailure>> ProcessRequestAsync(ServiceRequest request)
    {
        throw new System.NotImplementedException();
    }
}