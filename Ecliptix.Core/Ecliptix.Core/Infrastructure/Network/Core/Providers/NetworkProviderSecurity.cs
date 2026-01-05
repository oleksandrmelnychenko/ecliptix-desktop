using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Network.Security.Crypto;
using Ecliptix.Security.Certificate.Pinning.Services;

namespace Ecliptix.Network.Network.Core.Providers;

public sealed record NetworkProviderSecurity(
    ICertificatePinningServiceFactory CertificatePinningServiceFactory,
    IRsaChunkEncryptor RsaChunkEncryptor,
    IRetryPolicyProvider RetryPolicyProvider);
