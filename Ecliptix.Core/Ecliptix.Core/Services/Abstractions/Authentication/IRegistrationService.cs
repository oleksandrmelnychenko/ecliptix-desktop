using System;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Utilities;
using Google.Protobuf;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IRegistrationService
{
    Task<Result<ByteString, string>> ValidatePhoneNumberAsync(string mobileNumber, string deviceIdentifier, uint connectId);
    Task<Result<Guid, string>> InitiateOtpVerificationAsync(ByteString phoneNumberIdentifier, string deviceIdentifier);
    Task<Result<Protobuf.Membership.Membership, string>> VerifyOtpAsync(string otpCode, string deviceIdentifier, uint connectId);
    Task<Result<Unit, string>> CompleteRegistrationAsync(ByteString membershipIdentifier, SecureTextBuffer secureKey);
}