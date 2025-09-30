using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IAuthenticationService
{
    Task<Result<Unit, AuthenticationFailure>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword, uint connectId);
}