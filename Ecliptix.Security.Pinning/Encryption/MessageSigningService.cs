using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;

namespace Ecliptix.Security.Pinning.Encryption;

public sealed class MessageSigningService : IMessageSigning
{
    private readonly X509Certificate2? _clientCertificate;

    public MessageSigningService(X509Certificate2? clientCertificate = null)
    {
        _clientCertificate = clientCertificate;
    }

    public Task<byte[]> SignAsync(ReadOnlyMemory<byte> data, SigningAlgorithm algorithm)
    {
        if (_clientCertificate?.HasPrivateKey != true)
            throw new InvalidOperationException("Client certificate with private key required for signing");

        byte[] result = algorithm switch
        {
            SigningAlgorithm.RsaSha256 => SignWithRsa(data.Span, HashAlgorithmName.SHA256),
            SigningAlgorithm.EcdsaSha256 => SignWithEcdsa(data.Span, HashAlgorithmName.SHA256),
            SigningAlgorithm.EcdsaSha384 => SignWithEcdsa(data.Span, HashAlgorithmName.SHA384),
            _ => throw new NotSupportedException($"Signing algorithm {algorithm} not supported")
        };
        return Task.FromResult(result);
    }

    public Task<bool> VerifyAsync(ReadOnlyMemory<byte> data, ReadOnlyMemory<byte> signature, string publicKeyHex)
    {
        try
        {
            byte[] publicKeyBytes = Convert.FromHexString(publicKeyHex);

            using RSA rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

            return Task.FromResult(rsa.VerifyData(data.Span, signature.Span, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private byte[] SignWithRsa(ReadOnlySpan<byte> data, HashAlgorithmName hashAlgorithm)
    {
        using RSA? rsa = _clientCertificate?.GetRSAPrivateKey();
        if (rsa == null)
            throw new InvalidOperationException("RSA private key not available");

        return rsa.SignData(data, hashAlgorithm, RSASignaturePadding.Pkcs1);
    }

    private byte[] SignWithEcdsa(ReadOnlySpan<byte> data, HashAlgorithmName hashAlgorithm)
    {
        using ECDsa? ecdsa = _clientCertificate?.GetECDsaPrivateKey();
        if (ecdsa == null)
            throw new InvalidOperationException("ECDSA private key not available");

        return ecdsa.SignData(data, hashAlgorithm);
    }
}