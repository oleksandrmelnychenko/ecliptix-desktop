using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Core.Messaging;
using Ecliptix.Core.Core.Messaging.Events;
using Ecliptix.Core.Infrastructure.Data.Abstractions;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Infrastructure.Security.Abstractions;
using Ecliptix.Core.Infrastructure.Security.KeySplitting;
using Ecliptix.Core.Models.Membership;
using Ecliptix.Core.Services.Abstractions.Authentication;
using Ecliptix.Core.Services.Abstractions.Core;
using Ecliptix.Core.Services.Abstractions.Membership;
using Ecliptix.Core.Services.Common;
using Ecliptix.Core.Services.Core;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Device;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Protocol.System.Utilities;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;

namespace Ecliptix.Core.Services.Membership;

public class LogoutService(
    NetworkProvider networkProvider,
    IIdentityService identityService,
    IUnifiedMessageBus messageBus,
    ISecureProtocolStateStorage secureProtocolStateStorage,
    IDistributedShareStorage distributedShareStorage,
    IHmacKeyManager hmacKeyManager,
    IApplicationSecureStorageProvider applicationSecureStorageProvider,
    IApplicationInitializer applicationInitializer)
    : ILogoutService
{
    public async Task<Result<Unit, LogoutFailure>> LogoutAsync(LogoutReason reason, CancellationToken ct = default)
    {
        string? membershipId = await GetCurrentMembershipIdAsync();
        if (string.IsNullOrEmpty(membershipId))
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.InvalidMembershipIdentifier("No active session found"));
        }

        LogoutRequest logoutRequest = new()
        {
            MembershipIdentifier = ByteString.CopyFrom(Guid.Parse(membershipId).ToByteArray()),
            LogoutReason = reason.ToString()
        };

        Result<uint, NetworkFailure> protocolResult =
            await networkProvider.EnsureProtocolForTypeAsync(PubKeyExchangeType.DataCenterEphemeralConnect);

        if (protocolResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.NetworkRequestFailed("Failed to establish connection",
                    new Exception(protocolResult.UnwrapErr().Message)));
        }

        uint connectId = protocolResult.Unwrap();

        TaskCompletionSource<LogoutResponse> responseSource = new();

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.Logout,
            SecureByteStringInterop.WithByteStringAsSpan(logoutRequest.ToByteString(), span => span.ToArray()),
            payload =>
            {
                try
                {
                    LogoutResponse response = LogoutResponse.Parser.ParseFrom(payload);
                    responseSource.TrySetResult(response);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
                }
                catch (Exception ex)
                {
                    responseSource.TrySetException(ex);
                    return Task.FromResult(Result<Unit, NetworkFailure>.Err(
                        NetworkFailure.InvalidRequestType("Failed to parse logout response", ex)));
                }
            });

        if (networkResult.IsErr)
        {
            return Result<Unit, LogoutFailure>.Err(
                LogoutFailure.NetworkRequestFailed("Failed to communicate with server",
                    new Exception(networkResult.UnwrapErr().Message)));
        }

        LogoutResponse logoutResponse = await responseSource.Task;

        if (logoutResponse.Result != LogoutResponse.Types.Result.Succeeded)
        {
            return Result<Unit, LogoutFailure>.Err(logoutResponse.Result switch
            {
                LogoutResponse.Types.Result.AlreadyLoggedOut =>
                    LogoutFailure.AlreadyLoggedOut(logoutResponse.Message),
                LogoutResponse.Types.Result.SessionNotFound =>
                    LogoutFailure.SessionNotFound(logoutResponse.Message),
                _ => LogoutFailure.UnexpectedError(logoutResponse.Message)
            });
        }

        await secureProtocolStateStorage.DeleteStateAsync(connectId.ToString());

        Result<Unit, Ecliptix.Utilities.Failures.Authentication.AuthenticationFailure> clearResult =
            await identityService.ClearAllCacheAsync(membershipId);

        await distributedShareStorage.ClearAllCacheAsync();
        await hmacKeyManager.RemoveHmacKeyAsync(membershipId);

        Guid membershipGuid = Guid.Parse(membershipId);
        
        await distributedShareStorage.RemoveKeySharesAsync(membershipGuid);
        await applicationSecureStorageProvider.SetApplicationMembershipAsync(null);

        networkProvider.ClearConnection(connectId);

        if (applicationInitializer is ApplicationInitializer concreteInitializer)
        {
            PropertyInfo? property =
                typeof(ApplicationInitializer).GetProperty(nameof(IApplicationInitializer.IsMembershipConfirmed));
            property?.SetValue(concreteInitializer, false);
        }


        await messageBus.PublishAsync(new UserLoggedOutEvent(membershipId, reason.ToString()), ct);

        return Result<Unit, LogoutFailure>.Ok(Unit.Value);
    }

    private async Task<string?> GetCurrentMembershipIdAsync()
    {
        Result<ApplicationInstanceSettings, InternalServiceApiFailure> settingsResult =
            await applicationSecureStorageProvider.GetApplicationInstanceSettingsAsync();

        if (settingsResult.IsErr)
            return null;

        ApplicationInstanceSettings settings = settingsResult.Unwrap();

        if (settings.MembershipIdentifier == null || settings.MembershipIdentifier.IsEmpty)
            return null;

        return SecureByteStringInterop.WithByteStringAsSpan(settings.MembershipIdentifier,
            span => new Guid(span.ToArray()).ToString());
    }
}