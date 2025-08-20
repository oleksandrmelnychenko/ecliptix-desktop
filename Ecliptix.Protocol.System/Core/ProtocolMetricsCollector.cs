using System.Collections.Concurrent;
using System.Diagnostics;
using Serilog;
using Serilog.Events;

namespace Ecliptix.Protocol.System.Core;

public struct ProtocolMetrics
{
    public long TotalOutboundMessages { get; init; }
    public long TotalInboundMessages { get; init; }
    public long TotalBatchedMessages { get; init; }
    public long TotalEncryptionOperations { get; init; }
    public long TotalDecryptionOperations { get; init; }
    public long TotalRatchetRotations { get; init; }
    public long TotalCircuitBreakerTrips { get; init; }
    public double AverageLatencyMs { get; init; }
    public double ThroughputPerSecond { get; init; }
    public double ErrorRate { get; init; }
    public TimeSpan Uptime { get; init; }
    public long MemoryUsageBytes { get; init; }
    public LoadLevel CurrentLoadLevel { get; init; }
    public CircuitBreakerState CircuitBreakerState { get; init; }
}

public sealed class ProtocolMetricsCollector : IDisposable
{
    private readonly Lock _lock = new();
    private readonly ConcurrentQueue<double> _latencySamples = new();
    private readonly Timer _metricsTimer;
    private readonly Stopwatch _uptimeStopwatch = Stopwatch.StartNew();

    private long _outboundMessages;
    private long _inboundMessages;
    private long _batchedMessages;
    private long _encryptionOperations;
    private long _decryptionOperations;
    private long _ratchetRotations;
    private long _circuitBreakerTrips;
    private long _totalErrors;
    private long _totalOperations;

    private double _averageLatencyMs;
    private double _currentThroughput;
    private double _currentErrorRate;

    private LoadLevel _currentLoadLevel = LoadLevel.Light;
    private CircuitBreakerState _circuitBreakerState = CircuitBreakerState.Closed;

    private bool _disposed;

    public ProtocolMetricsCollector(TimeSpan metricsUpdateInterval = default)
    {
        TimeSpan interval = metricsUpdateInterval == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : metricsUpdateInterval;

        _metricsTimer = new Timer(
            callback: _ => UpdateMetrics(),
            state: null,
            dueTime: interval,
            period: interval
        );
    }

    public void RecordOutboundMessage(double latencyMs = 0)
    {
        Interlocked.Increment(ref _outboundMessages);
        Interlocked.Increment(ref _totalOperations);

        if (latencyMs > 0)
        {
            RecordLatency(latencyMs);
        }
    }

    public void RecordInboundMessage(double latencyMs = 0)
    {
        Interlocked.Increment(ref _inboundMessages);
        Interlocked.Increment(ref _totalOperations);

        if (latencyMs > 0)
        {
            RecordLatency(latencyMs);
        }
    }

    public void RecordBatchOperation(int messageCount, double totalLatencyMs = 0)
    {
        Interlocked.Add(ref _batchedMessages, messageCount);
        Interlocked.Increment(ref _totalOperations);

        if (totalLatencyMs > 0)
        {
            RecordLatency(totalLatencyMs);
        }
    }

    public void RecordEncryption()
    {
        Interlocked.Increment(ref _encryptionOperations);
    }

    public void RecordDecryption()
    {
        Interlocked.Increment(ref _decryptionOperations);
    }

    public void RecordRatchetRotation()
    {
        Interlocked.Increment(ref _ratchetRotations);
    }

    public void RecordCircuitBreakerTrip()
    {
        Interlocked.Increment(ref _circuitBreakerTrips);
    }

    public void RecordError()
    {
        Interlocked.Increment(ref _totalErrors);
        Interlocked.Increment(ref _totalOperations);
    }

    public void UpdateExternalState(LoadLevel loadLevel, CircuitBreakerState circuitState)
    {
        lock (_lock)
        {
            _currentLoadLevel = loadLevel;
            _circuitBreakerState = circuitState;
        }
    }

    private void RecordLatency(double latencyMs)
    {
        _latencySamples.Enqueue(latencyMs);

        if (_latencySamples.Count > 10000)
        {
            while (_latencySamples.Count > 5000)
            {
                _latencySamples.TryDequeue(out _);
            }
        }
    }

    private void UpdateMetrics()
    {
        if (_disposed) return;

        try
        {
            lock (_lock)
            {
                double totalLatency = 0;
                int sampleCount = 0;

                foreach (double latency in _latencySamples)
                {
                    totalLatency += latency;
                    sampleCount++;
                }

                _averageLatencyMs = sampleCount > 0 ? totalLatency / sampleCount : 0;

                TimeSpan uptime = _uptimeStopwatch.Elapsed;
                _currentThroughput = uptime.TotalSeconds > 0 ? _totalOperations / uptime.TotalSeconds : 0;

                _currentErrorRate = _totalOperations > 0 ? (double)_totalErrors / _totalOperations : 0;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Protocol metrics update error");
        }
    }

    public ProtocolMetrics GetCurrentMetrics()
    {
        lock (_lock)
        {
            return new ProtocolMetrics
            {
                TotalOutboundMessages = _outboundMessages,
                TotalInboundMessages = _inboundMessages,
                TotalBatchedMessages = _batchedMessages,
                TotalEncryptionOperations = _encryptionOperations,
                TotalDecryptionOperations = _decryptionOperations,
                TotalRatchetRotations = _ratchetRotations,
                TotalCircuitBreakerTrips = _circuitBreakerTrips,
                AverageLatencyMs = _averageLatencyMs,
                ThroughputPerSecond = _currentThroughput,
                ErrorRate = _currentErrorRate,
                Uptime = _uptimeStopwatch.Elapsed,
                MemoryUsageBytes = GC.GetTotalMemory(false),
                CurrentLoadLevel = _currentLoadLevel,
                CircuitBreakerState = _circuitBreakerState
            };
        }
    }

    public void LogMetricsSummary()
    {
        ProtocolMetrics metrics = GetCurrentMetrics();

        if (Log.IsEnabled(LogEventLevel.Information))
        {
            Log.Information("Protocol Performance Metrics - Uptime: {Uptime}, Load: {LoadLevel}, " +
                           "CB State: {CircuitBreakerState}, Throughput: {Throughput:F1} ops/sec",
                metrics.Uptime.ToString(@"hh\:mm\:ss"), metrics.CurrentLoadLevel,
                metrics.CircuitBreakerState, metrics.ThroughputPerSecond);
            
            Log.Information("Protocol Latency & Errors - Avg Latency: {AvgLatency:F2} ms, " +
                           "Error Rate: {ErrorRate:P2}, Memory: {MemoryMB:F1} MB",
                metrics.AverageLatencyMs, metrics.ErrorRate,
                metrics.MemoryUsageBytes / (1024.0 * 1024.0));
            
            Log.Information("Protocol Message Stats - Out: {OutMessages}, In: {InMessages}, " +
                           "Batch: {BatchMessages}, Encrypt: {EncryptOps}, Decrypt: {DecryptOps}, " +
                           "Ratchets: {RatchetRotations}, CB Trips: {CBTrips}",
                metrics.TotalOutboundMessages, metrics.TotalInboundMessages,
                metrics.TotalBatchedMessages, metrics.TotalEncryptionOperations,
                metrics.TotalDecryptionOperations, metrics.TotalRatchetRotations,
                metrics.TotalCircuitBreakerTrips);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            Interlocked.Exchange(ref _outboundMessages, 0);
            Interlocked.Exchange(ref _inboundMessages, 0);
            Interlocked.Exchange(ref _batchedMessages, 0);
            Interlocked.Exchange(ref _encryptionOperations, 0);
            Interlocked.Exchange(ref _decryptionOperations, 0);
            Interlocked.Exchange(ref _ratchetRotations, 0);
            Interlocked.Exchange(ref _circuitBreakerTrips, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
            Interlocked.Exchange(ref _totalOperations, 0);

            _averageLatencyMs = 0;
            _currentThroughput = 0;
            _currentErrorRate = 0;

            while (_latencySamples.TryDequeue(out _))
            {
            }

            _uptimeStopwatch.Restart();

            if (Log.IsEnabled(LogEventLevel.Information))
                Log.Information("Protocol metrics reset");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _metricsTimer?.Dispose();
        _uptimeStopwatch?.Stop();

        if (Log.IsEnabled(LogEventLevel.Debug))
            Log.Debug("Protocol metrics collector disposed");
    }
}