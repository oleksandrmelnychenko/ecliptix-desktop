using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Services.Authentication;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Google.Protobuf;

namespace Ecliptix.Core.Services.Abstractions.Authentication;

public interface IPasswordRecoveryService
{
    Task<Result<ByteString, string>> ValidateMobileForRecoveryAsync(string mobileNumber, string deviceIdentifier,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> InitiatePasswordResetOtpAsync(ByteString mobileNumberIdentifier,
        string deviceIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> ResendPasswordResetOtpAsync(Guid sessionIdentifier, ByteString mobileNumberIdentifier,
        string deviceIdentifier,
        Action<uint, Guid, VerificationCountdownUpdate.Types.CountdownUpdateStatus, string?>? onCountdownUpdate = null,
        CancellationToken cancellationToken = default);

    Task<Result<Protobuf.Membership.Membership, string>> VerifyPasswordResetOtpAsync(Guid sessionIdentifier, string otpCode,
        string deviceIdentifier,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CompletePasswordResetAsync(ByteString membershipIdentifier, SecureTextBuffer newPassword,
        uint connectId, CancellationToken cancellationToken = default);

    Task<Result<Unit, string>> CleanupPasswordResetSessionAsync(Guid sessionIdentifier);
}
