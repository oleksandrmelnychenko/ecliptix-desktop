
using Ecliptix.Utilities;

namespace Ecliptix.Security.Certificate.Pinning.Services;
public interface ICertificatePinningServiceFactory : IAsyncDisposable
{
    Task<Option<CertificatePinningService>> GetOrInitializeServiceAsync();
}
