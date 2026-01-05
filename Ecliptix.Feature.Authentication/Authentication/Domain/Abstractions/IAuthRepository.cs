using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Protobuf.Transport.Identity;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Google.Protobuf;

namespace Ecliptix.Feature.Authentication.Authentication.Domain.Abstractions;

public interface IAuthRepository
{
    Task<Result<Unit, AuthenticationFailure>> SignInAsync(string phoneNumber, SecureTextBuffer secureKey, uint connectId, CancellationToken cancellationToken);
    Task<Result<ValidateMobileNumberResponse, string>> ValidateMobileNumberAsync(string mobileNumber, uint connectId, CancellationToken cancellationToken = default);
    Task<Result<CheckMobileNumberAvailabilityResponse, string>> CheckMobileNumberAvailabilityAsync(ByteString mobileNumberIdentifier, uint connectId, CancellationToken cancellationToken = default);
    Task<Result<Unit, string>> InitiateRegistrationOtpAsync(
        ByteString mobileNumberIdentifier,
        VerificationPurpose purpose,
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default);
    Task<Result<Unit, string>> ResendRegistrationOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default);
    Task<Result<Membership, string>> VerifyRegistrationOtpAsync(Guid sessionIdentifier, string otpCode, uint connectId, CancellationToken cancellationToken = default);
    Task<Result<Unit, string>> CleanupRegistrationSessionAsync(Guid sessionIdentifier, CancellationToken cancellationToken = default);
    Task<Result<Unit, string>> CompleteRegistrationAsync(ByteString membershipIdentifier, SecureTextBuffer secureKey, uint connectId, CancellationToken cancellationToken = default);

    Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(string mobileNumber, uint connectId, CancellationToken cancellationToken = default);
    Task<Result<Unit, string>> InitiateSecureKeyResetOtpAsync(
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default);
    Task<Result<Unit, string>> ResendSecureKeyResetOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default);
    Task<Result<Membership, string>> VerifySecureKeyResetOtpAsync(Guid sessionIdentifier, string otpCode, uint connectId, CancellationToken cancellationToken = default);
    Task<Result<Unit, string>> CleanupSecureKeyResetSessionAsync(Guid sessionIdentifier, CancellationToken cancellationToken = default);
    Task<Result<Unit, string>> CompleteSecureKeyResetAsync(ByteString membershipIdentifier, SecureTextBuffer secureKey, uint connectId, CancellationToken cancellationToken = default);
}
