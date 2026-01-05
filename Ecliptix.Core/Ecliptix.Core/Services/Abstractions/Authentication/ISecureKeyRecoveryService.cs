using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Protobuf.Transport.Identity;
using Ecliptix.Utilities;
using Google.Protobuf;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface ISecureKeyRecoveryService
{
    Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(string mobileNumber,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> InitiateSecureKeyResetOtpAsync(ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> ResendSecureKeyResetOtpAsync(Guid sessionIdentifier, ByteString mobileNumberIdentifier,
        Action<uint, Guid, CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Protobuf.Transport.Identity.Membership, string>> VerifySecureKeyResetOtpAsync(Guid sessionIdentifier, string otpCode,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CompleteSecureKeyResetAsync(ByteString membershipIdentifier, SecureTextBuffer newSecureKey,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CleanupSecureKeyResetSessionAsync(Guid sessionIdentifier);
}
