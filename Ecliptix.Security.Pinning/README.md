# Ecliptix.Security.Pinning

Application-layer security library providing first-request encryption for gRPC communications.

## Features

- **Application-Layer Encryption**: End-to-end encryption for sensitive messages
- **First Request Protection**: Encrypts protocol public keys before handshake
- **Hybrid Encryption**: AES-GCM + RSA/ECDSA for optimal performance and security
- **gRPC Integration**: Seamless interceptor-based implementation

## Usage

### Application-Layer Security

```csharp
// In Program.cs
services.AddApplicationLayerSecurity(configuration, options =>
{
    options.EnableApplicationLayerSecurity = true;
    options.SecureFirstMessageOnly = true;
    options.Algorithm = EncryptionAlgorithm.AesGcm256;
    options.SigningAlgorithm = SigningAlgorithm.EcdsaSha384;
});

// In appsettings.json
{
  "ApplicationSecurity": {
    "EnableApplicationLayerSecurity": true,
    "SecureFirstMessageOnly": true,
    "ServerPublicKeyHex": "3082010a02820101...",
    "ClientCertificateThumbprint": "ABC123...",
    "FirstRequestMethods": ["ExchangeKeys", "InitProtocol"]
  }
}
```

### gRPC Client Integration

```csharp
var channel = GrpcChannel.ForAddress("https://server:5001", new GrpcChannelOptions
{
    Interceptors = { serviceProvider.GetRequiredService<SecureHandshakeInterceptor>() }
});

var client = new YourGrpcClient(channel);

// First request will be automatically encrypted
var response = await client.ExchangeKeysAsync(new KeyExchangeRequest
{
    PublicKey = ByteString.CopyFrom(publicKeyBytes)
});
```

## Architecture

The library implements defense-in-depth with multiple security layers:

1. **Transport Layer**: SSL/TLS with certificate pinning
2. **Application Layer**: Message-level encryption and signing
3. **Protocol Layer**: Integration with existing Ecliptix protocol system

Messages are encrypted using hybrid cryptography:
- **AES-GCM**: For payload encryption (fast, secure)
- **RSA/ECDSA**: For key exchange and signatures (strong authentication)

## Security Model

- **Confidentiality**: AES-GCM encryption prevents message disclosure
- **Integrity**: Digital signatures ensure message authenticity
- **Non-repudiation**: Client certificates provide sender authentication
- **Forward Secrecy**: Ephemeral AES keys per message
- **Replay Protection**: Built-in nonces and timestamps