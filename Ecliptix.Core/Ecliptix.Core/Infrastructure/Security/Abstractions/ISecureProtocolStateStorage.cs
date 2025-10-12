using System;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Security.Storage;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Infrastructure.Security.Abstractions;

public interface ISecureProtocolStateStorage
{
    Task<Result<Unit, SecureStorageFailure>> SaveStateAsync(byte[] protocolState, string connectId, byte[] membershipId);

    Task<Result<byte[], SecureStorageFailure>> LoadStateAsync(string connectId, byte[] membershipId);

    Task<Result<Unit, SecureStorageFailure>> DeleteStateAsync(string key);
}