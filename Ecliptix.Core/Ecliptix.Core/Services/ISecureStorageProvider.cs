using System;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;

namespace Ecliptix.Core.Services;

public interface ISecureStorageProvider : IAsyncDisposable, IDisposable
{
    Task<bool> StoreAsync(string key, byte[] data);
    Task<Result<Option<byte[]>, InternalServiceApiFailure>> TryGetByKeyAsync(string key);
    Task<bool> DeleteAsync(string key);
}