using System.Threading;
using System.Threading.Tasks;

namespace Ecliptix.Core.Services.Network.Infrastructure;

internal interface ITypedPendingRequest
{
    Task ExecuteAsync(CancellationToken cancellationToken);
    void Cancel();
}
