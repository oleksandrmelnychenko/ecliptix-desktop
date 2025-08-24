# Ecliptix SSL Pinning Integration Guide

This guide shows how to integrate the comprehensive SSL pinning solution into your Ecliptix application.

## 🔐 **Complete SSL Pinning Solution Overview**

Your implementation now includes:

### **OpenSSL Certificate Management Tools**
- `Scripts/ssl-pinning/generate-certs.sh` - Complete PKI infrastructure
- `Scripts/ssl-pinning/extract-pins.sh` - Pin extraction & C# code generation  
- `Scripts/ssl-pinning/rotate-certs.sh` - Zero-downtime certificate rotation

### **Ecliptix.Security.Pinning Library**
- **Certificate Pinning**: Multi-layer validation (SPKI, cert, CA)
- **First Request Encryption**: Additional layer beyond TLS
- **gRPC Integration**: Seamless integration with existing infrastructure
- **Configuration Driven**: Multiple enforcement levels

## 🚀 **Quick Start Integration**

### Step 1: Generate Your Certificates

```bash
# Generate complete certificate infrastructure
cd Scripts/ssl-pinning
./generate-certs.sh

# Extract pins for client
./extract-pins.sh

# Copy generated pins to library
cp pins/PinnedCertificates.cs ../../Ecliptix.Security.Pinning/Certificates/
```

### Step 2: Configure SSL Pinning

Add to your `appsettings.json`:

```json
{
  "SslPinning": {
    "Enabled": true,
    "EnableFirstRequestEncryption": true,
    "EnforcementLevel": "Strict",
    "UseBackupPins": true,
    "UseRootCaFallback": true,
    "ConnectionTimeoutMs": 30000,
    "AllowedTlsVersions": ["Tls12", "Tls13"],
    "FirstRequestEncryption": {
      "SignatureAlgorithm": "EcdsaWithSha384",
      "EncryptionAlgorithm": "RsaOaepWithSha256",
      "IncludeTimestamp": true,
      "IncludeNonce": true,
      "MaxRequestAge": 300
    }
  }
}
```

### Step 3: Update Program.cs

Replace your existing gRPC registration:

```csharp
// OLD: Basic gRPC clients
// services.AddConfiguredGrpcClients();

// NEW: Secure gRPC clients with SSL pinning
services.AddSslPinning(configuration)
    .ValidateConfiguration();

services.AddSecureGrpcClients();
```

### Step 4: Update GrpcClientServiceExtensions.cs

Replace the old implementation:

```csharp
using Ecliptix.Security.Pinning.Transport;

public static class GrpcClientServiceExtensions
{
    public static void AddConfiguredGrpcClients(this IServiceCollection services)
    {
        // Use the new secure gRPC client extensions
        services.AddSecureGrpcClients();
    }
}
```

## 🔧 **Certificate Management Workflow**

### Production Deployment

1. **Generate Production Certificates**:
   ```bash
   ./Scripts/ssl-pinning/generate-certs.sh
   ```

2. **Deploy Server Certificates**:
   ```bash
   # Copy to your server infrastructure
   scp generated/certs/server-*.crt user@server:/etc/ssl/certs/
   scp generated/keys/server-*.key user@server:/etc/ssl/private/
   ```

3. **Update Client Pins**:
   ```bash
   ./Scripts/ssl-pinning/extract-pins.sh
   # Rebuild and deploy client with new pins
   ```

### Certificate Rotation (Zero Downtime)

1. **Prepare New Certificates**:
   ```bash
   ./Scripts/ssl-pinning/rotate-certs.sh prepare
   ```

2. **Deploy to Server**:
   ```bash
   # Deploy the generated deployment package
   tar -xzf rotation/deployment_package.tar.gz -C /server/path/
   # Reload your server configuration
   ```

3. **Activate New Certificates**:
   ```bash
   ./Scripts/ssl-pinning/rotate-certs.sh activate --server-host api.ecliptix.com
   ```

4. **Complete Rotation**:
   ```bash
   ./Scripts/ssl-pinning/rotate-certs.sh complete
   # Rebuild client with updated pins
   ```

## 🛡️ **Security Features**

### Multi-Layer Pin Validation

The system validates certificates in this order:
1. **Primary Server Pins** - Current server certificates
2. **Backup Server Pins** - Rotation certificates  
3. **Intermediate CA Pins** - Intermediate certificate authority
4. **Root CA Pins** - Ultimate fallback

### First Request Encryption

Beyond TLS, initial requests are additionally:
- **Encrypted** with server's public key (RSA-OAEP)
- **Signed** with ephemeral client key (ECDSA)
- **Protected** against replay attacks (timestamp + nonce)

### Enforcement Levels

- **Disabled**: No pinning (for development)
- **LogOnly**: Log violations but allow connections
- **Strict**: Block connections on pin mismatch
- **KillSwitch**: Block all connections if pinning fails

## 📊 **Monitoring & Debugging**

### Check Pin Status

```bash
# Verify pins match your server
./Scripts/ssl-pinning/pins/verify-pins.sh api.ecliptix.com 443

# Test with different server
./Scripts/ssl-pinning/pins/verify-pins.sh localhost 8443
```

### Application Logs

The library provides detailed logging:

```csharp
// Configure logging levels
services.AddLogging(builder =>
{
    builder.AddFilter("Ecliptix.Security.Pinning", LogLevel.Debug);
});
```

### Enforcement Testing

Test different enforcement levels in development:

```json
{
  "SslPinning": {
    "EnforcementLevel": "LogOnly",  // Test without blocking
    "AllowConnectionsOnFailure": true  // Emergency override
  }
}
```

## 🔥 **Advanced Configuration**

### Custom Pin Storage

```csharp
public class CustomPinStorage : IPinStorage
{
    // Load pins from database, HSM, etc.
}

services.AddSslPinning<CustomPinStorage>(configuration);
```

### Multiple Enforcement Policies

```csharp
services.AddSslPinning(config =>
{
    config.Enabled = true;
    config.EnforcementLevel = PinningEnforcementLevel.Strict;
    
    // Development overrides
    if (environment.IsDevelopment())
    {
        config.EnforcementLevel = PinningEnforcementLevel.LogOnly;
        config.AllowConnectionsOnFailure = true;
    }
});
```

### Performance Tuning

```csharp
services.Configure<PinningConfiguration>(config =>
{
    config.ConnectionTimeoutMs = 15000;  // Faster timeout
    config.UseBackupPins = false;       // Skip backup checks
    config.EnableFirstRequestEncryption = false; // Disable for performance
});
```

## 🚨 **Security Best Practices**

### Certificate Storage

- **Root CA Keys**: Store offline in secure hardware
- **Server Keys**: Use HSM or secure key management
- **Client Pins**: Embed encrypted in application resources

### Rotation Planning

- **Schedule**: Rotate certificates every 90 days
- **Testing**: Test rotation in staging environment first
- **Monitoring**: Monitor pin validation success rates
- **Rollback**: Always have rollback plan ready

### Incident Response

If pin validation fails in production:

1. **Immediate**: Set enforcement to LogOnly
2. **Investigate**: Check if legitimate certificate change
3. **Verify**: Validate new certificate authenticity
4. **Update**: Regenerate pins if certificate is valid
5. **Deploy**: Push updated pins to clients

## 📋 **Troubleshooting**

### Common Issues

**Pin Validation Failures**:
- Check server certificate hasn't changed
- Verify time synchronization (for timestamp validation)
- Check network connectivity and DNS resolution

**Build Errors**:
- Ensure all NuGet packages are up to date
- Check protobuf files are valid
- Verify project references are correct

**Performance Issues**:
- Disable first request encryption if not needed
- Reduce connection timeout
- Skip backup pin validation

### Debug Commands

```bash
# Check certificate details
openssl x509 -in server.crt -text -noout

# Test TLS connection
openssl s_client -connect api.ecliptix.com:443 -servername api.ecliptix.com

# Extract current server pin
echo | openssl s_client -connect api.ecliptix.com:443 2>/dev/null | \
  openssl x509 -pubkey -noout | \
  openssl pkey -pubin -outform der | \
  openssl dgst -sha256 -binary | \
  base64
```

---

## 🎉 **Congratulations!**

You now have a **world-class SSL pinning implementation** that provides:

✅ **Bank-grade security** with multiple validation layers  
✅ **Zero-downtime** certificate rotation  
✅ **First request encryption** beyond TLS  
✅ **Complete certificate management** with OpenSSL tools  
✅ **Production-ready** monitoring and debugging  
✅ **Flexible configuration** for different environments  

Your application is now protected against MITM attacks and has complete control over its certificate infrastructure!