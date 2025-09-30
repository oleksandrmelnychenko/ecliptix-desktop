using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Infrastructure.Security.KeySplitting;

public interface IShareAuthenticationService
{
    Task<Result<byte[], string>> GenerateHmacKeyAsync(string identifier);

    Task<Result<Unit, string>> StoreHmacKeyAsync(string identifier, byte[] hmacKey);

    Task<Result<byte[], string>> RetrieveHmacKeyAsync(string identifier);

    Task<Result<bool, string>> HasHmacKeyAsync(string identifier);

    Task<Result<Unit, string>> RemoveHmacKeyAsync(string identifier);
}