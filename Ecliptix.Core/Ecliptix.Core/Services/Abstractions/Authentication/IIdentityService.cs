using System.Threading.Tasks;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IIdentityService
{
    Task<bool> HasStoredIdentityAsync(string membershipId);
    Task StoreIdentityAsync(byte[] masterKey, string membershipId);
    Task<byte[]?> LoadMasterKeyAsync(string membershipId);
}