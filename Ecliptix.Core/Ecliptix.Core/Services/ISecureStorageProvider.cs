using System;
using System.Threading.Tasks;
using Ecliptix.Core.Protocol.Utilities;

namespace Ecliptix.Core.Services;

public interface ISecureStorageProvider : IAsyncDisposable, IDisposable
{
    bool Store(string key, byte[] data);
    Result<Option<byte[]>, InternalServiceApiFailure> TryGetByKey(string key);
    Result<bool, InternalServiceApiFailure> Delete(string key);
}