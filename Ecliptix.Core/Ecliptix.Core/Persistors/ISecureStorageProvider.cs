using System;
using System.Threading.Tasks;
using Ecliptix.Core.Services;
using Ecliptix.Protocol.System.Utilities;

namespace Ecliptix.Core.Persistors;

public interface ISecureStorageProvider : IAsyncDisposable
{
    Task<Result<Unit, InternalServiceApiFailure>> StoreAsync(string key, byte[] data);

    Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key);

    Task<Result<Unit, InternalServiceApiFailure>> DeleteAsync(string key);
}