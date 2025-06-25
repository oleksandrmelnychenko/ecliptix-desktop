using System;
using System.Threading.Tasks;

namespace Ecliptix.Core.Services
{
    public interface IBytesStorageService
    {
        Task<bool> StoreAsync(string key, byte[] data);
        Task<byte[]?> RetrieveAsync(string key);
        Task<bool> DeleteAsync(string key);
        Task ClearAllAsync();
    }
}