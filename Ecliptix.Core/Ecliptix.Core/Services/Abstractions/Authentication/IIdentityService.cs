using System.Threading.Tasks;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IIdentityService
{
    Task<bool> HasStoredIdentityAsync(string membershipId);
    Task<Result<Unit, AuthenticationFailure>> ClearAllCacheAsync(string membershipId);

    Task<Result<Unit, AuthenticationFailure>> StoreIdentityAsync(SodiumSecureMemoryHandle masterKeyHandle, string membershipId);
    Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string membershipId);
}