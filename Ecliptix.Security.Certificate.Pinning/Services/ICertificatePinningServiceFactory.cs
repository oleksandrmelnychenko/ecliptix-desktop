namespace Ecliptix.Security.Certificate.Pinning.Services;

using Utilities;

public interface ICertificatePinningServiceFactory : IAsyncDisposable
{
    Task<Option<CertificatePinningService>> GetOrInitializeServiceAsync();
}
