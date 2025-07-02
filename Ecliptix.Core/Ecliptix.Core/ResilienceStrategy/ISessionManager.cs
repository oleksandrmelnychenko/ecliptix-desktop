using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;
using Ecliptix.Protobuf.AppDevice;

namespace Ecliptix.Core.ResilienceStrategy;

public interface ISessionManager
{
    Task<Result<Unit, EcliptixProtocolFailure>> ReEstablishSessionAsync();

    void MarkSessionAsUnhealthy();
}