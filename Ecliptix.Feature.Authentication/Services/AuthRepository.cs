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
using CountdownUpdateStatus = Ecliptix.Protobuf.Membership.VerificationCountdownUpdate.Types.CountdownUpdateStatus;

namespace Ecliptix.Feature.Authentication.Services;

public sealed class AuthRepository(
    IAuthenticationService authService,
    IOpaqueRegistrationService registrationService,
    ISecureKeyRecoveryService recoveryService)
    : IAuthRepository
{
    private readonly IAuthenticationService _authService = authService;
    private readonly IOpaqueRegistrationService _registrationService = registrationService;
    private readonly ISecureKeyRecoveryService _recoveryService = recoveryService;

    public Task<Result<Unit, AuthenticationFailure>> SignInAsync(
        string phoneNumber,
        SecureTextBuffer secureKey,
        uint connectId,
        CancellationToken cancellationToken)
        => _authService.SignInAsync(phoneNumber, secureKey, connectId, cancellationToken);

    public Task<Result<ValidateMobileNumberResponse, string>> ValidateMobileNumberAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        _registrationService.ValidateMobileNumberAsync(mobileNumber, connectId, cancellationToken);

    public Task<Result<CheckMobileNumberAvailabilityResponse, string>> CheckMobileNumberAvailabilityAsync(
        ByteString mobileNumberIdentifier,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        _registrationService.CheckMobileNumberAvailabilityAsync(mobileNumberIdentifier, connectId, cancellationToken);

    public Task<Result<Unit, string>> InitiateRegistrationOtpAsync(
        ByteString mobileNumberIdentifier,
        VerificationPurpose purpose,
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default) =>
        _registrationService.InitiateOtpVerificationAsync(
            mobileNumberIdentifier,
            purpose,
            AdaptCountdownCallback(onCountdownUpdate),
            cancellationToken);

    public Task<Result<Unit, string>> ResendRegistrationOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default) =>
        _registrationService.ResendOtpVerificationAsync(
            sessionIdentifier,
            mobileNumberIdentifier,
            AdaptCountdownCallback(onCountdownUpdate),
            cancellationToken);

    public Task<Result<MembershipProto, string>> VerifyRegistrationOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        _registrationService.VerifyOtpAsync(sessionIdentifier, otpCode, connectId, cancellationToken);

    public Task<Result<Unit, string>> CleanupRegistrationSessionAsync(
        Guid sessionIdentifier,
        CancellationToken cancellationToken = default) =>
        _registrationService.CleanupVerificationSessionAsync(sessionIdentifier);

    public Task<Result<Unit, string>> CompleteRegistrationAsync(
        ByteString membershipIdentifier,
        SecureTextBuffer secureKey,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        _registrationService.CompleteRegistrationAsync(membershipIdentifier, secureKey, connectId, cancellationToken);

    public Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(
        string mobileNumber,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        _recoveryService.ValidateMobileForRecoveryAsync(mobileNumber, connectId, cancellationToken);

    public Task<Result<Unit, string>> InitiateSecureKeyResetOtpAsync(
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default) =>
        _recoveryService.InitiateSecureKeyResetOtpAsync(
            mobileNumberIdentifier,
            AdaptCountdownCallback(onCountdownUpdate),
            cancellationToken);

    public Task<Result<Unit, string>> ResendSecureKeyResetOtpAsync(
        Guid sessionIdentifier,
        ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? onCountdownUpdate,
        CancellationToken cancellationToken = default) =>
        _recoveryService.ResendSecureKeyResetOtpAsync(
            sessionIdentifier,
            mobileNumberIdentifier,
            AdaptCountdownCallback(onCountdownUpdate),
            cancellationToken);

    public Task<Result<MembershipProto, string>> VerifySecureKeyResetOtpAsync(
        Guid sessionIdentifier,
        string otpCode,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        _recoveryService.VerifySecureKeyResetOtpAsync(sessionIdentifier, otpCode, connectId, cancellationToken);

    public Task<Result<Unit, string>> CleanupSecureKeyResetSessionAsync(
        Guid sessionIdentifier,
        CancellationToken cancellationToken = default) =>
        _recoveryService.CleanupSecureKeyResetSessionAsync(sessionIdentifier);

    public Task<Result<Unit, string>> CompleteSecureKeyResetAsync(
        ByteString membershipIdentifier,
        SecureTextBuffer secureKey,
        uint connectId,
        CancellationToken cancellationToken = default) =>
        _recoveryService.CompleteSecureKeyResetAsync(membershipIdentifier, secureKey, connectId, cancellationToken);

    private static Action<uint, Guid, CountdownUpdateStatus, string?>? AdaptCountdownCallback(
        Action<uint, Guid, CountdownUpdateStatus, string?, string?, bool>? callback)
    {
        if (callback == null)
        {
            return null;
        }

        return (seconds, identifier, status, message) =>
            callback(seconds, identifier, status, message, null, false);
    }
}
