using System;
using System.Threading.Tasks;
using Ecliptix.Utilities;

namespace Ecliptix.Core.Services;

public interface IAuthenticationService
{
    Task<Result<byte[], string>> SignInAsync(string mobileNumber, SecureTextBuffer securePassword);
}