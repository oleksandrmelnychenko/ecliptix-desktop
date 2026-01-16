using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Feature.Authentication.Services.Authentication;
using Ecliptix.Utilities;
using Google.Protobuf;
using OtpCountdownStatus = Ecliptix.Protobuf.Membership.OtpCountdownUpdate.Types.Status;

namespace Ecliptix.Feature.Authentication.Services.Abstractions.Authentication;

public interface ISecureKeyRecoveryService
{
    Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(string mobileNumber,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> InitiateSecureKeyResetOtpAsync(ByteString mobileNumberIdentifier,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> ResendSecureKeyResetOtpAsync(Guid sessionIdentifier, ByteString mobileNumberIdentifier,
        Action<uint, Guid, OtpCountdownStatus, string?, string?, bool>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Protobuf.Membership.Membership, string>> VerifySecureKeyResetOtpAsync(Guid sessionIdentifier, string otpCode,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CompleteSecureKeyResetAsync(ByteString membershipIdentifier, SecureTextBuffer newSecureKey,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CleanupSecureKeyResetSessionAsync(Guid sessionIdentifier);
}
