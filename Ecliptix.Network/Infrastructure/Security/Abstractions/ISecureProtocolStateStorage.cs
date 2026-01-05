using Ecliptix.Network.Infrastructure.Security.Storage;
using Ecliptix.Utilities;

namespace Ecliptix.Network.Infrastructure.Security.Abstractions;

public interface ISecureProtocolStateStorage
{
    Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(byte[] protocolState, string connectId, byte[] membershipId);

    Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string connectId, byte[] membershipId);

    Task<Result<Unit, SecureStorageFailure>> DeleteStateAsync(string key);
}
