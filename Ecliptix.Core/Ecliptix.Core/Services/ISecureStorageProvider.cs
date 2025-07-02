using System;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;

namespace Ecliptix.Core.Services;

public interface ISecureStorageProvider : IAsyncDisposable
{
    Task<Result<Unit, InternalServiceApiFailure>> StoreAsync(string key, byte[] data);

    Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key);

    Task<Result<Unit, InternalServiceApiFailure>> DeleteAsync(string key);
}