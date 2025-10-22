namespace Ecliptix.Security.Certificate.Pinning.Services;

using Ecliptix.Utilities;

public interface ICertificatePinningServiceFactory : IAsyncDisposable
{
    Option<CertificatePinningService> GetOrInitializeService();
}
