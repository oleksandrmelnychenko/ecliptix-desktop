using System.Collections.Concurrent;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Core;

public sealed class ReplayProtection : IDisposable
{
    private readonly ConcurrentDictionary<string, DateTime> _processedNonces;
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
        _processedNonces = new ConcurrentDictionary<string, DateTime>();
        _messageWindows = new ConcurrentDictionary<ulong, MessageWindow>();
        _nonceLifetime = nonceLifetime == TimeSpan.Zero ? TimeSpan.FromMinutes(5) : nonceLifetime;
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
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1)
        );
    }

    public Result<Unit, EcliptixProtocolFailure> CheckAndRecordMessage(
        byte[] nonce,
        ulong messageIndex,
        ulong chainIndex = 0)
    {
        if (_disposed)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.ObjectDisposed(nameof(ReplayProtection)));

        if (nonce.Length == 0)
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Nonce cannot be null or empty"));

        lock (_lock)
        {
            string nonceKey = Convert.ToBase64String(nonce);

            if (_processedNonces.ContainsKey(nonceKey))
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic($"Replay attack detected: nonce already processed"));
            }

            Result<Unit, EcliptixProtocolFailure> windowCheck = CheckMessageWindow(chainIndex, messageIndex);
            if (windowCheck.IsErr)
                return windowCheck;

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
                    EcliptixProtocolFailure.Generic($"Replay attack detected: message index {messageIndex} already processed for chain {chainIndex}"));
            }

            ulong gap = window.HighestProcessedIndex - messageIndex;
            if (gap > _maxOutOfOrderWindow)
            {
                return Result<Unit, EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic($"Message index {messageIndex} is too far behind (gap: {gap}, max: {_maxOutOfOrderWindow})"));
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
        if (_disposed) return;

        DateTime cutoff = DateTime.UtcNow - _nonceLifetime;

        lock (_lock)
        {
            List<string> expiredKeys = new(_processedNonces.Count);
            expiredKeys.AddRange(from kvp in _processedNonces where kvp.Value < cutoff select kvp.Key);

            foreach (string expiredKey in expiredKeys)
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
        if (_disposed) return;

        DateTime now = DateTime.UtcNow;
        if (now - _lastWindowAdjustment < TimeSpan.FromMinutes(2))
            return;

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
        if (_disposed) return;

        lock (_lock)
        {
            _messageWindows.Clear();
            Console.WriteLine("[REPLAY] Cleared message windows due to ratchet rotation");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _cleanupTimer?.Dispose();
        _processedNonces.Clear();
        _messageWindows.Clear();
    }

    private sealed class MessageWindow
    {
        private readonly SortedSet<ulong> _processedIndices = [];
        private readonly DateTime _createdAt = DateTime.UtcNow;

        public ulong HighestProcessedIndex { get; private set; }

        public MessageWindow(ulong initialIndex)
        {
            HighestProcessedIndex = initialIndex;
            _processedIndices.Add(initialIndex);
        }

        public bool IsProcessed(ulong messageIndex)
        {
            return _processedIndices.Contains(messageIndex);
        }

        public void MarkProcessed(ulong messageIndex)
        {
            _processedIndices.Add(messageIndex);
            if (messageIndex > HighestProcessedIndex)
            {
                HighestProcessedIndex = messageIndex;
            }
        }

        public void CleanupOldEntries(DateTime cutoff)
        {
            if (_createdAt < cutoff)
            {
                ulong keepFromIndex = HighestProcessedIndex > 1000 ? HighestProcessedIndex - 1000 : 0;
                _processedIndices.RemoveWhere(idx => idx < keepFromIndex);
            }
        }
    }
}