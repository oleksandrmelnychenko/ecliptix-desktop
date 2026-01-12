using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Network.Infrastructure.Network.Core.Providers;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;
using OtpCountdownStatus = Ecliptix.Protobuf.Membership.OtpCountdownUpdate.Types.Status;

namespace Ecliptix.Feature.Authentication.Services.Authentication.Internal;

internal sealed class VerificationStreamManager(NetworkProvider networkProvider) : IDisposable
{
    private readonly ConcurrentDictionary<Guid, uint> _activeStreams = new();
    private readonly ConcurrentDictionary<Guid, OtpVerificationPurpose> _activeSessionPurposes = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly List<Task> _backgroundCleanupTasks = new();
    private bool _isDisposed;

    public bool TryGetActiveStream(Guid sessionIdentifier, out uint connectId) =>
        _activeStreams.TryGetValue(sessionIdentifier, out connectId);

    public OtpVerificationPurpose GetSessionPurpose(Guid sessionIdentifier) =>
        _activeSessionPurposes.GetValueOrDefault(sessionIdentifier, OtpVerificationPurpose.Registration);

    public void RegisterStream(Guid verificationIdentifier, uint streamConnectId, OtpVerificationPurpose purpose)
    {
        if (_isDisposed || verificationIdentifier == Guid.Empty)
        {
            return;
        }

        _activeStreams.TryAdd(verificationIdentifier, streamConnectId);
        _activeSessionPurposes.TryAdd(verificationIdentifier, purpose);
    }

    public void ProcessVerificationUpdate(
        Guid verificationIdentifier,
        uint streamConnectId,
        OtpCountdownStatus status,
        OtpVerificationPurpose purpose)
    {
        bool shouldCleanup = ShouldCleanupStream(status);

        if (shouldCleanup)
        {
            if (verificationIdentifier != Guid.Empty)
            {
                ScheduleStreamCleanup(verificationIdentifier);
            }
        }
        else if (verificationIdentifier != Guid.Empty)
        {
            RegisterStream(verificationIdentifier, streamConnectId, purpose);
        }
    }

    public async Task<Result<Unit, string>> CloseStreamAsync(Guid sessionIdentifier)
    {
        _activeSessionPurposes.TryRemove(sessionIdentifier, out OtpVerificationPurpose _);

        if (!_activeStreams.TryRemove(sessionIdentifier, out uint connectId))
        {
            return Result<Unit, string>.Ok(Unit.Value);
        }

        Result<Unit, NetworkFailure> result = await networkProvider
            .CleanupStreamProtocolAsync(connectId)
            .ConfigureAwait(false);

        return result.IsErr
            ? Result<Unit, string>.Err(result.UnwrapErr().Message)
            : Result<Unit, string>.Ok(Unit.Value);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _disposalCts.Cancel();

        Task[] tasksToWait;
        lock (_backgroundCleanupTasks)
        {
            tasksToWait = _backgroundCleanupTasks.Where(t => !t.IsCompleted).ToArray();
        }

        if (tasksToWait.Length > 0)
        {
            Task.WaitAll(tasksToWait, TimeSpan.FromSeconds(5));
        }

        _disposalCts.Dispose();
        _activeStreams.Clear();
        _activeSessionPurposes.Clear();
    }

    private static bool ShouldCleanupStream(OtpCountdownStatus status)
    {
        return status switch
        {
            OtpCountdownStatus.OtpCountdownStatusFailed => true,
            OtpCountdownStatus.OtpCountdownStatusMaxAttemptsReached => true,
            OtpCountdownStatus.OtpCountdownStatusNotFound => true,
            _ => false
        };
    }

    private void ScheduleStreamCleanup(Guid verificationIdentifier)
    {
        Task cleanupTask = Task.Run(async () =>
        {
            try
            {
                await CloseStreamAsync(verificationIdentifier).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[STREAM-CLEANUP] Failed to cleanup stream {VerificationId}",
                    verificationIdentifier);
            }
        }, _disposalCts.Token);

        lock (_backgroundCleanupTasks)
        {
            _backgroundCleanupTasks.Add(cleanupTask);
            _backgroundCleanupTasks.RemoveAll(t => t.IsCompleted);
        }
    }
}
