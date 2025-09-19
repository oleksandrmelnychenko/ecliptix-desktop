using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface ISessionKeyService
{
    Task StoreSessionKeyAsync(byte[] sessionKey, uint connectId);
    Task<byte[]?> GetSessionKeyAsync(uint connectId);
    Task InvalidateSessionKeyAsync(uint connectId);
    Task InvalidateAllSessionKeysAsync();
    Task<bool> HasValidSessionKeyAsync(uint connectId);
}