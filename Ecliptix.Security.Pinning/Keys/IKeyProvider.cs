using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Ecliptix.Security.Pinning.Keys;

public interface IKeyProvider
{
    Task<string> GetServerPublicKeyAsync();
    Task<X509Certificate2?> GetClientCertificateAsync();
    Task<RSA?> GetServerPrivateKeyAsync();
}

public sealed class ConfigurationKeyProvider : IKeyProvider
{
    private readonly string _serverPublicKeyHex;
    private readonly X509Certificate2? _clientCertificate;

    public ConfigurationKeyProvider(string serverPublicKeyHex, X509Certificate2? clientCertificate = null)
    {
        _serverPublicKeyHex = serverPublicKeyHex ?? throw new ArgumentNullException(nameof(serverPublicKeyHex));
        _clientCertificate = clientCertificate;
    }

    public Task<string> GetServerPublicKeyAsync() => Task.FromResult(_serverPublicKeyHex);

    public Task<X509Certificate2?> GetClientCertificateAsync() => Task.FromResult(_clientCertificate);

    public Task<RSA?> GetServerPrivateKeyAsync()
    {
        return Task.FromResult(_clientCertificate?.GetRSAPrivateKey());
    }
}

public sealed class CertificateKeyProvider : IKeyProvider
{
    private readonly X509Certificate2 _serverCertificate;
    private readonly X509Certificate2? _clientCertificate;

    public CertificateKeyProvider(X509Certificate2 serverCertificate, X509Certificate2? clientCertificate = null)
    {
        _serverCertificate = serverCertificate ?? throw new ArgumentNullException(nameof(serverCertificate));
        _clientCertificate = clientCertificate;
    }

    public Task<string> GetServerPublicKeyAsync()
    {
        using RSA? rsa = _serverCertificate.GetRSAPublicKey();
        if (rsa == null)
            throw new InvalidOperationException("Server certificate does not contain RSA public key");

        byte[] publicKeyBytes = rsa.ExportSubjectPublicKeyInfo();
        return Task.FromResult(Convert.ToHexString(publicKeyBytes));
    }

    public Task<X509Certificate2?> GetClientCertificateAsync() => Task.FromResult(_clientCertificate);

    public Task<RSA?> GetServerPrivateKeyAsync()
    {
        return Task.FromResult(_serverCertificate.GetRSAPrivateKey());
    }
}