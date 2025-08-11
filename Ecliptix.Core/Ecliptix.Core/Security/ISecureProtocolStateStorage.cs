using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Security;

public interface ISecureProtocolStateStorage
{
    Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(byte[] protocolState, string connectId);

    Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string connectId);

    Task<Result<Unit, SecureStorageFailure>> DeleteStateAsync(string key);
}