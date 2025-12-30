using System;
using System.Threading.Tasks;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IIdentityService
{
    Task<bool> HasStoredIdentityAsync(string accountId);

    Task<Result<Unit, AuthenticationFailure>> StoreIdentityAsync(SodiumSecureMemoryHandle masterKeyHandle, string accountId);
    Task<Result<SodiumSecureMemoryHandle, AuthenticationFailure>> LoadMasterKeyHandleAsync(string accountId);

    Task<Result<Unit, Exception>> CleanupMembershipStateWithKeysAsync(string accountId, uint connectId);
}
