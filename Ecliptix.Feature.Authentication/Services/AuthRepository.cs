using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Authentication.Domain.Abstractions;
using Ecliptix.Feature.Authentication.Services.Abstractions.Authentication;
using Ecliptix.Feature.Authentication.Services.Authentication;
using Ecliptix.Protobuf.Membership;
using MembershipProto = Ecliptix.Protobuf.Membership.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Authentication;
using Google.Protobuf;
using OtpCountdownStatus = Ecliptix.Protobuf.Membership.OtpCountdownUpdate.Types.Status;

namespace Ecliptix.Feature.Authentication.Services;

public sealed class AuthRepository(
    IAuthenticationService authService,
    IOpaqueRegistrationService registrationService,
    ISecureKeyRecoveryService recoveryService)
    : IAuthRepository
{
    public Task<Result<Unit, AuthenticationFailure>> SignInAsync(
        string phoneNumber,
        SecureTextBuffer secureKey,
        uint connectId,
        CancellationToken cancellationToken)
        => authService.SignInAsync(phoneNumber, secureKey, connectId, cancellationToken);

    public Task<Result<MobileNumberValidateResponse, string>> ValidateMobileNumberAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        registrationService.ValidateMobileNumberAsync(mobileNumber, connectId, cancellationToken);

    public Task<Result<MobileNumberAvailabilityResponse, string>> CheckMobileNumberAvailabilityAsync(
        ByteString mobileNumberIdentifier,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        registrationService.CheckMobileNumberAvailabilityAsync(mobileNumberIdentifier, connectId, cancellationToken);

    public Task<Result<Unit, string>> InitiateRegistrationOtpAsync(
        ByteString mobileNumberIdentifier,
        OtpVerificationPurpose purpose,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default) =>
        registrationService.InitiateOtpVerificationAsync(
            mobileNumberIdentifier,
            purpose,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<Unit, string>> ResendRegistrationOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default) =>
        registrationService.ResendOtpVerificationAsync(
            sessionIdentifier,
            mobileNumberIdentifier,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<MembershipProto, string>> VerifyRegistrationOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        registrationService.VerifyOtpAsync(sessionIdentifier, otpCode, connectId, cancellationToken);

    public Task<Result<Unit, string>> CleanupRegistrationSessionAsync(
        Guid sessionIdentifier,
        CancellationToken cancellationToken = default) =>
        registrationService.CleanupVerificationSessionAsync(sessionIdentifier);

    public Task<Result<Unit, string>> CompleteRegistrationAsync(
        ByteString membershipIdentifier,
        SecureTextBuffer secureKey,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        registrationService.CompleteRegistrationAsync(membershipIdentifier, secureKey, connectId, cancellationToken);

    public Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        recoveryService.ValidateMobileForRecoveryAsync(mobileNumber, connectId, cancellationToken);

    public Task<Result<Unit, string>> InitiateSecureKeyResetOtpAsync(
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default) =>
        recoveryService.InitiateSecureKeyResetOtpAsync(
            mobileNumberIdentifier,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<Unit, string>> ResendSecureKeyResetOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default) =>
        recoveryService.ResendSecureKeyResetOtpAsync(
            sessionIdentifier,
            mobileNumberIdentifier,
            onCountdownUpdate,
            cancellationToken);

    public Task<Result<MembershipProto, string>> VerifySecureKeyResetOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        recoveryService.VerifySecureKeyResetOtpAsync(sessionIdentifier, otpCode, connectId, cancellationToken);

    public Task<Result<Unit, string>> CleanupSecureKeyResetSessionAsync(
        Guid sessionIdentifier,
        CancellationToken cancellationToken = default) =>
        recoveryService.CleanupSecureKeyResetSessionAsync(sessionIdentifier);

    public Task<Result<Unit, string>> CompleteSecureKeyResetAsync(
        ByteString membershipIdentifier,
        SecureTextBuffer secureKey,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        recoveryService.CompleteSecureKeyResetAsync(membershipIdentifier, secureKey, connectId, cancellationToken);
}
