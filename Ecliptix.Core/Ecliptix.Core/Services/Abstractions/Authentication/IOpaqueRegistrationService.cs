using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IOpaqueRegistrationService
{
    Task<Result<ByteString, string>> ValidatePhoneNumberAsync(string mobileNumber, string deviceIdentifier,
        uint connectId);

    Task<Result<Unit, string>> InitiateOtpVerificationAsync(ByteString phoneNumberIdentifier, string deviceIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null);

    Task<Result<Unit, string>> ResendOtpVerificationAsync(Guid sessionIdentifier, ByteString phoneNumberIdentifier,
        string deviceIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null);

    Task<Result<Protobuf.Membership.Membership, string>> VerifyOtpAsync(Guid sessionIdentifier, string otpCode,
        string deviceIdentifier,
        uint connectId);

    Task<Result<Unit, string>> CompleteRegistrationAsync(ByteString membershipIdentifier, SecureTextBuffer secureKey,
        uint connectId);

    Task<Result<Unit, string>> CleanupVerificationSessionAsync(Guid sessionIdentifier);
}