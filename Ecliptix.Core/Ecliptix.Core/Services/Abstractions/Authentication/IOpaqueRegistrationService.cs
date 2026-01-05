using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Protobuf.Transport.Identity;
using Ecliptix.Utilities;
using Google.Protobuf;
using VerificationPurpose = Ecliptix.Protobuf.Transport.Identity.VerificationPurpose;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IOpaqueRegistrationService
{
    Task<Result<ValidateMobileNumberResponse, string>> ValidateMobileNumberAsync(string mobileNumber,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<CheckMobileNumberAvailabilityResponse, string>> CheckMobileNumberAvailabilityAsync(
        ByteString mobileNumberIdentifier,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> InitiateOtpVerificationAsync(
        ByteString mobileNumberIdentifier,
        VerificationPurpose purpose = VerificationPurpose.Registration,
        Action<uint, Guid, CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> ResendOtpVerificationAsync(Guid sessionIdentifier, ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Protobuf.Transport.Identity.Membership, string>> VerifyOtpAsync(Guid sessionIdentifier, string otpCode,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CompleteRegistrationAsync(ByteString membershipIdentifier, SecureTextBuffer secureKey,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CleanupVerificationSessionAsync(Guid sessionIdentifier);
}
