using System.Threading.Tasks;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Core.Network.ResilienceStrategy;

public interface INetworkProvider
{
    Task<Result<Unit, EcliptixProtocolFailure>> RestoreSecrecyChannelAsync();

    void SetSecrecyChannelAsUnhealthy();
}