using Ecliptix.Protected.Protocol.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;

namespace Ecliptix.Network.Services.Abstractions.Authentication;

public interface IIdentityService
{
    Task<bool> HasStoredIdentityAsync(string accountId);

    Task<Result<Unit, AuthenticationFailure>> StoreIdentityAsync(SodiumSecureMemoryHandle masterKeyHandle, string accountId);
    Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string accountId);

    Task<Result<Unit, Exception>> CleanupMembershipStateWithKeysAsync(string accountId, uint connectId);
}
