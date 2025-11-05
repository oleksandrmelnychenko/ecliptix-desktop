using System;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Data;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Core.Services.Network.Rpc;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Membership;
using Ecliptix.Utilities.Failures.Network;
using Google.Protobuf;
using Serilog;

namespace Ecliptix.Core.Services.Network;

internal sealed class PendingLogoutProcessor(
    NetworkProvider networkProvider,
    PendingLogoutRequestStorage pendingLogoutStorage)
{
    public async Task ProcessPendingLogoutAsync(
        uint connectId,
        CancellationToken cancellationToken = default)
    {
        Result<Option<LogoutRequest>, LogoutFailure> getResult =
            await pendingLogoutStorage.GetPendingLogoutAsync().ConfigureAwait(false);

        if (getResult.IsErr || !getResult.Unwrap().IsSome)
        {
            return;
        }

        LogoutRequest pendingRequest = getResult.Unwrap().Value!;

        TaskCompletionSource<bool> responseCompletionSource =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        Result<Unit, NetworkFailure> networkResult = await networkProvider.ExecuteUnaryRequestAsync(
            connectId,
            RpcServiceType.AnonymousLogout,
            pendingRequest.ToByteArray(),
            async responsePayload =>
            {
                AnonymousLogoutResponse.Parser.ParseFrom(responsePayload);
                responseCompletionSource.TrySetResult(true);
                return await Task.FromResult(Result<Unit, NetworkFailure>.Ok(Unit.Value));
            },
            allowDuplicates: false,
            token: cancellationToken).ConfigureAwait(false);

        if (networkResult.IsOk)
        {
            await responseCompletionSource.Task.ConfigureAwait(false);
        }
        else
        {
            _ = Task.Run(async () =>
            {
                await EnsureProtocolInBackground();
            }, cancellationToken).ContinueWith(
                task =>
                {
                    if (task is { IsFaulted: true, Exception: not null })
                    {
                        Log.Error(task.Exception, "[PENDING-LOGOUT-RETRY] Unhandled exception ensuring protocol");
                    }
                },
                TaskScheduler.Default);
        }

        pendingLogoutStorage.ClearPendingLogout();
    }

    private async Task EnsureProtocolInBackground()
    {
        await networkProvider.EnsureProtocolForTypeAsync(
            PubKeyExchangeType.DataCenterEphemeralConnect);
    }
}
