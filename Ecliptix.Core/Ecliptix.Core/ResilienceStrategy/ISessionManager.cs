using System.Threading.Tasks;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Protocol.System.Utilities;

namespace Ecliptix.Core.ResilienceStrategy;

public interface ISessionManager
{
    Task<Result<Unit, EcliptixProtocolFailure>> ReEstablishSessionAsync();

    void MarkSessionAsUnhealthy();
}