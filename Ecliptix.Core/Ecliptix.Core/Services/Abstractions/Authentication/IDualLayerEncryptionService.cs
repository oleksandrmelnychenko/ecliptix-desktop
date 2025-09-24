using System.Threading.Tasks;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IDualLayerEncryptionService
{
    Task<Result<byte[], string>> EncryptHeaderAsync(EnvelopeMetadata metadata, uint connectId);

    Task<Result<byte[], string>> EncryptHeaderAsync(byte[] headerBytes, uint connectId);

    Task<Result<EnvelopeMetadata, string>> DecryptHeaderAsync(byte[] encryptedHeader, uint connectId);

    Task<Result<SecureEnvelope, string>> CreateSecureEnvelopeAsync(EnvelopeMetadata metadata, byte[] encryptedPayload, uint connectId);

    Task<Result<(EnvelopeMetadata Metadata, byte[] EncryptedPayload), string>> ProcessSecureEnvelopeAsync(SecureEnvelope secureEnvelope, uint connectId);

    Task<bool> IsSessionKeyAvailableAsync(uint connectId);
}