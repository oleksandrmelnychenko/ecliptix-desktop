using System;
using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.Core;

public interface IStateCleanupService
{
    Task<Result<Unit, Exception>> CleanupMembershipStateWithKeysAsync(string membershipId, uint connectId);
}
