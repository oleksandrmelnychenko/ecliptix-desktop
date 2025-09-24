namespace Ecliptix.Security.Certificate.Pinning.Services;

public interface ICertificatePinningServiceFactory : IAsyncDisposable
{
    Task<CertificatePinningService?> GetOrInitializeServiceAsync();
}