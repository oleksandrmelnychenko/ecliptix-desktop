namespace Ecliptix.Security.Certificate.Pinning.Services;

public interface ICertificatePinningServiceFactory : IAsyncDisposable
{
    CertificatePinningService? GetOrInitializeService();
}
