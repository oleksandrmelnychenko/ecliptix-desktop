using Ecliptix.Network.Infrastructure.Security.Abstractions;
using Ecliptix.Network.Infrastructure.Security.Crypto;
using Ecliptix.Network.Services.Abstractions.Network;
using Ecliptix.Security.Certificate.Pinning.Services;

namespace Ecliptix.Network.Infrastructure.Network.Core.Providers;

public sealed record NetworkProviderSecurity(
    ICertificatePinningServiceFactory CertificatePinningServiceFactory,
    IRsaChunkEncryptor RsaChunkEncryptor,
    IRetryPolicyProvider RetryPolicyProvider,
    IPlatformSecurityProvider PlatformSecurityProvider);
