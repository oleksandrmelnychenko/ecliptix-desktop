using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Security;

public interface ISecureProtocolStateStorage
{
    Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(byte[] protocolState, string userId);

    Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string userId);

    Task<Result<Unit, SecureStorageFailure>> DeleteStateAsync(string key);
}