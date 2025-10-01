using Ecliptix.Security.Certificate.Pinning.Services;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Infrastructure.Security.Crypto;

public interface IRsaChunkEncryptor
{
    Result<byte[], NetworkFailure> EncryptInChunks(
        CertificatePinningService certificatePinningService,
        byte[] originalData);

    Result<byte[], NetworkFailure> DecryptInChunks(
        CertificatePinningService certificatePinningService,
        byte[] combinedEncryptedData);
}
