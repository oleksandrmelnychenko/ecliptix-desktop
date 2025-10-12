using System;
using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.Core;

public interface IStateCleanupService
{
    Task<Result<Unit, Exception>> CleanupUserStateAsync(string membershipId, uint connectId);
    Task<Result<Unit, Exception>> CleanupUserStateWithKeysAsync(string membershipId, uint connectId);
}
