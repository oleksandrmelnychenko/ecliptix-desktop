using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IAuthenticationService
{
    Task<Result<Unit, string>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword, uint connectId);
}