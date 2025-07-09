using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Network.ResilienceStrategy;

public interface INetworkProvider
{
    Task<Result<Unit, NetworkFailure>> RestoreSecrecyChannelAsync();

    void SetSecrecyChannelAsUnhealthy();
}