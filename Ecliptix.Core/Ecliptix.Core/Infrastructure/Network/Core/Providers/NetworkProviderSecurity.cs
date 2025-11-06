using Ecliptix.Core.Infrastructure.Security.Crypto;
using Ecliptix.Core.Services.Abstractions.Network;
using Ecliptix.Security.Certificate.Pinning.Services;

namespace Ecliptix.Core.Infrastructure.Network.Core.Providers;

public sealed record NetworkProviderSecurity(
    ICertificatePinningServiceFactory CertificatePinningServiceFactory,
    IRsaChunkEncryptor RsaChunkEncryptor,
    IRetryPolicyProvider RetryPolicyProvider);
