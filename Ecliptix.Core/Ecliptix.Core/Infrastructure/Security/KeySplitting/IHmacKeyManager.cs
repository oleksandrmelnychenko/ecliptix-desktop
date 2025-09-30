using System.Threading.Tasks;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public interface IHmacKeyManager
{
    Task<Result<Unit, KeySplittingFailure>> RemoveHmacKeyAsync(string identifier);

    Task<Result<SodiumSecureMemoryHandle, KeySplittingFailure>> GenerateHmacKeyHandleAsync(string identifier);

    Task<Result<SodiumSecureMemoryHandle, KeySplittingFailure>> RetrieveHmacKeyHandleAsync(string identifier);
}