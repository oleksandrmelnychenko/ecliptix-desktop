using System;
using Ecliptix.Security.Pinning.Encryption;

namespace Ecliptix.Security.Pinning.Configuration;

public sealed class ApplicationSecurityOptions
{
    public bool EnableApplicationLayerSecurity { get; set; } = false;
    public bool SecureFirstMessageOnly { get; set; } = true;
    public bool SecureAllMessages { get; set; } = false;
    public EncryptionAlgorithm Algorithm { get; set; } = EncryptionAlgorithm.AesGcm256;
    public SigningAlgorithm SigningAlgorithm { get; set; } = SigningAlgorithm.EcdsaSha384;
    public TimeSpan MessageTimeout { get; set; } = TimeSpan.FromMinutes(5);
    public string? ServerPublicKeyHex { get; set; }
    public string? ClientCertificateThumbprint { get; set; }
    public string[] FirstRequestMethods { get; set; } = ["ExchangeKeys", "InitProtocol"];

    public EncryptionOptions ToEncryptionOptions() => new(
        Algorithm,
        SigningAlgorithm,
        SecureFirstMessageOnly,
        SecureAllMessages,
        MessageTimeout);
}