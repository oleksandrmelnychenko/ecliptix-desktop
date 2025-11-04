using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Core.Infrastructure.Network.Core.Providers;
using Ecliptix.Protobuf.Membership;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Services.Authentication.Internal;

internal sealed class VerificationStreamManager : IDisposable
{
    private readonly NetworkProvider _networkProvider;
    private readonly ConcurrentDictionary<Guid, uint> _activeStreams = new();
    private readonly ConcurrentDictionary<Guid, VerificationPurpose> _activeSessionPurposes = new();
    private readonly CancellationTokenSource _disposalCts = new();
    private readonly List<Task> _backgroundCleanupTasks = new();
    private bool _isDisposed;

    public VerificationStreamManager(NetworkProvider networkProvider)
    {
        _networkProvider = networkProvider;
    }

    public bool TryGetActiveStream(Guid sessionIdentifier, out uint connectId)
    {
        return _activeStreams.TryGetValue(sessionIdentifier, out connectId);
    }

    public VerificationPurpose GetSessionPurpose(Guid sessionIdentifier)
    {
        return _activeSessionPurposes.GetValueOrDefault(sessionIdentifier, VerificationPurpose.Registration);
    }

    public void RegisterStream(Guid verificationIdentifier, uint streamConnectId, VerificationPurpose purpose)
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
        VerificationCountdownUpdate.Types.CountdownUpdateStatus status,
        VerificationPurpose purpose)
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
        _activeSessionPurposes.TryRemove(sessionIdentifier, out VerificationPurpose _);

        if (!_activeStreams.TryRemove(sessionIdentifier, out uint connectId))
        {
            return Result<Unit, string>.Ok(Unit.Value);
        }

        Result<Unit, NetworkFailure> result = await _networkProvider
            .CleanupStreamProtocolAsync(connectId)
            .ConfigureAwait(false);

        if (result.IsErr)
        {
            return Result<Unit, string>.Err(result.UnwrapErr().Message);
        }

        return Result<Unit, string>.Ok(Unit.Value);
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

    private static bool ShouldCleanupStream(VerificationCountdownUpdate.Types.CountdownUpdateStatus status)
    {
        return status switch
        {
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.Failed => true,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.MaxAttemptsReached => true,
            VerificationCountdownUpdate.Types.CountdownUpdateStatus.NotFound => true,
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
                Serilog.Log.Error(ex, "[STREAM-CLEANUP] Failed to cleanup stream {VerificationId}", verificationIdentifier);
            }
        }, _disposalCts.Token);

        lock (_backgroundCleanupTasks)
        {
            _backgroundCleanupTasks.Add(cleanupTask);
            _backgroundCleanupTasks.RemoveAll(t => t.IsCompleted);
        }
    }
}
