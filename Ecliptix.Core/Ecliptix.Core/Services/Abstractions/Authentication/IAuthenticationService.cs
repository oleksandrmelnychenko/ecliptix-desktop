using System;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IAuthenticationService
{
    Task<Result<byte[], string>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword, uint connectId);
}