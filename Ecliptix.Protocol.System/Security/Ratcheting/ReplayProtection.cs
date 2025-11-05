using System.Collections.Concurrent;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Security.Ratcheting;

using Ecliptix.Protocol.System.Security.ReplayProtection;

internal sealed class ReplayProtection : IDisposable
{
    private readonly ConcurrentDictionary<NonceKey, DateTime> _processedNonces;
    private readonly ConcurrentDictionary<ulong, MessageWindow> _messageWindows;
    private readonly TimeSpan _nonceLifetime;
    private ulong _maxOutOfOrderWindow;
    private readonly Timer _cleanupTimer;
    private readonly Lock _lock = new();
    private readonly ulong _baseWindow;
    private readonly ulong _maxWindow;
    private int _recentMessageCount;
    private DateTime _lastWindowAdjustment = DateTime.UtcNow;
    private bool _disposed;

    public ReplayProtection(
        TimeSpan nonceLifetime = default,
        ulong maxOutOfOrderWindow = 1000,
        ulong maxWindow = 5000)
    {
        _processedNonces = new ConcurrentDictionary<NonceKey, DateTime>();
        _messageWindows = new ConcurrentDictionary<ulong, MessageWindow>();
        _nonceLifetime = nonceLifetime == TimeSpan.Zero ? ProtocolSystemConstants.Timeouts.NonceLifetime : nonceLifetime;
        _baseWindow = maxOutOfOrderWindow;
        _maxOutOfOrderWindow = maxOutOfOrderWindow;
        _maxWindow = maxWindow;

        _cleanupTimer = new Timer(
            callback: _ =>
            {
                CleanupExpiredEntries();
                AdjustWindowSize();
            },
            state: null,
            dueTime: ProtocolSystemConstants.Timeouts.CleanupInterval,
            period: ProtocolSystemConstants.Timeouts.CleanupInterval
        );
    }

    public Result<Unit, EcliptixProtocolFailure> CheckAndRecordMessage(
        byte[] nonce,
        ulong messageIndex,
        ulong chainIndex = 0)
    {
        if (_disposed)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.OBJECT_DISPOSED(nameof(ReplayProtection)));
        }

        if (nonce.Length == 0)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput(EcliptixProtocolFailureMessages.ReplayProtection.NONCE_CANNOT_BE_NULL_OR_EMPTY));
        }

        lock (_lock)
        {
            NonceKey nonceKey = new(nonce);

            if (_processedNonces.ContainsKey(nonceKey))
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.ReplayProtection.REPLAY_ATTACK_DETECTED_NONCE));
            }

            Result<Unit, EcliptixProtocolFailure> windowCheck = CheckMessageWindow(chainIndex, messageIndex);
            if (windowCheck.IsErr)
            {
                return windowCheck;
            }

            _processedNonces[nonceKey] = DateTime.UtcNow;
            UpdateMessageWindow(chainIndex, messageIndex);
            _recentMessageCount++;

            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }
    }

    private Result<Unit, EcliptixProtocolFailure> CheckMessageWindow(ulong chainIndex, ulong messageIndex)
    {
        if (!_messageWindows.TryGetValue(chainIndex, out MessageWindow? window))
        {
            _messageWindows[chainIndex] = new MessageWindow(messageIndex);
            return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
        }

        if (messageIndex <= window.HighestProcessedIndex)
        {
            if (window.IsProcessed(messageIndex))
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(string.Format(EcliptixProtocolFailureMessages.ReplayProtection.REPLAY_ATTACK_DETECTED_MESSAGE_INDEX, messageIndex, chainIndex)));
            }

            ulong gap = window.HighestProcessedIndex - messageIndex;
            if (gap > _maxOutOfOrderWindow)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic(string.Format(EcliptixProtocolFailureMessages.ReplayProtection.MESSAGE_INDEX_TOO_FAR_BEHIND, messageIndex, gap, _maxOutOfOrderWindow)));
            }
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    private void UpdateMessageWindow(ulong chainIndex, ulong messageIndex)
    {
        if (_messageWindows.TryGetValue(chainIndex, out MessageWindow? window))
        {
            window.MarkProcessed(messageIndex);
        }
        else
        {
            _messageWindows[chainIndex] = new MessageWindow(messageIndex);
        }
    }

    private void CleanupExpiredEntries()
    {
        if (_disposed)
        {
            return;
        }

        DateTime cutoff = DateTime.UtcNow - _nonceLifetime;

        lock (_lock)
        {
            List<NonceKey> expiredKeys = new(_processedNonces.Count);
            expiredKeys.AddRange(from kvp in _processedNonces where kvp.Value < cutoff select kvp.Key);

            foreach (NonceKey expiredKey in expiredKeys)
            {
                _processedNonces.TryRemove(expiredKey, out _);
            }

            foreach (KeyValuePair<ulong, MessageWindow> kvp in _messageWindows)
            {
                kvp.Value.CleanupOldEntries(cutoff);
            }
        }
    }

    private void AdjustWindowSize()
    {
        if (_disposed)
        {
            return;
        }

        DateTime now = DateTime.UtcNow;
        if (now - _lastWindowAdjustment < ProtocolSystemConstants.Timeouts.WindowAdjustmentInterval)
        {
            return;
        }

        lock (_lock)
        {
            double messageRate = _recentMessageCount / 2.0;

            if (messageRate > 50)
            {
                _maxOutOfOrderWindow = Math.Min(_baseWindow * 3, _maxWindow);
            }
            else if (messageRate > 20)
            {
                _maxOutOfOrderWindow = Math.Min(_baseWindow * 2, _maxWindow);
            }
            else
            {
                _maxOutOfOrderWindow = _baseWindow;
            }

            _recentMessageCount = 0;
            _lastWindowAdjustment = now;
        }
    }

    public void OnRatchetRotation()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            _messageWindows.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cleanupTimer?.Dispose();
        _processedNonces.Clear();
        _messageWindows.Clear();
    }
}
