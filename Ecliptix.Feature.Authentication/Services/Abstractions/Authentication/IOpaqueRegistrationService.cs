using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Authentication.Services.Authentication;
using Ecliptix.Protobuf.Membership;
using MembershipProto = Ecliptix.Protobuf.Membership.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;
using OtpCountdownStatus = Ecliptix.Protobuf.Membership.OtpCountdownUpdate.Types.Status;

namespace Ecliptix.Feature.Authentication.Services.Abstractions.Authentication;

public interface IOpaqueRegistrationService
{
    Task<Result<MobileNumberValidateResponse, string>> ValidateMobileNumberAsync(string mobileNumber,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<MobileNumberValidateResponse, string>> ValidateMobileForRecoveryAsync(string mobileNumber,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<MobileNumberAvailabilityResponse, string>> CheckMobileNumberAvailabilityAsync(
        ByteString mobileNumberIdentifier,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> InitiateOtpVerificationAsync(
        ByteString mobileNumberIdentifier,
        OtpVerificationPurpose purpose = OtpVerificationPurpose.Registration,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> ResendOtpVerificationAsync(Guid sessionIdentifier, ByteString mobileNumberIdentifier,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<MembershipProto, string>> VerifyOtpAsync(Guid sessionIdentifier, string otpCode,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CompleteRegistrationAsync(ByteString membershipIdentifier, SecureTextBuffer secureKey,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CleanupVerificationSessionAsync(Guid sessionIdentifier);
}
