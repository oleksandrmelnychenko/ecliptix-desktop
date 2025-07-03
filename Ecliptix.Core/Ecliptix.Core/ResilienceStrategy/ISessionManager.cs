using System.Threading.Tasks;
using Ecliptix.Protobuf.AppDevice;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Core.ResilienceStrategy;

public interface ISessionManager
{
    Task<Result<Unit, EcliptixProtocolFailure>> ReEstablishSessionAsync();

    void MarkSessionAsUnhealthy();
}