using System.Threading.Tasks;
using Ecliptix.Protobuf.Common;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IDualLayerEncryptionService
{
    Task<Result<byte[], string>> EncryptHeaderAsync(CipherHeader header, uint connectId);

    Task<Result<byte[], string>> EncryptHeaderAsync(byte[] headerBytes, uint connectId);

    Task<Result<CipherHeader, string>> DecryptHeaderAsync(byte[] encryptedHeader, uint connectId);

    Task<Result<CipherPayload, string>> CreateCipherPayloadAsync(CipherHeader header, byte[] encryptedPayload, uint connectId);

    Task<Result<(CipherHeader Header, byte[] EncryptedPayload), string>> ProcessCipherPayloadAsync(CipherPayload cipherPayload, uint connectId);

    Task<bool> IsSessionKeyAvailableAsync(uint connectId);
}