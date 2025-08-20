using System.Diagnostics;
using System.Text;

namespace Ecliptix.Protocol.System.Core;

public sealed class PerformanceProfiler
{
    private readonly Dictionary<string, ProfileData> _metrics = new();
    private readonly Lock _lock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    public IDisposable StartOperation(string operationName)
    {
        return new OperationTimer(this, operationName);
    }

    private void RecordOperation(string operationName, TimeSpan duration)
    {
        lock (_lock)
        {
            if (!_metrics.TryGetValue(operationName, out ProfileData? data))
            {
                data = new ProfileData();
                _metrics[operationName] = data;
            }

            data.RecordDuration(duration);
        }
    }

    public Dictionary<string, (long Count, double AvgMs, double MaxMs, double MinMs)> GetMetrics()
    {
        lock (_lock)
        {
            return _metrics.ToDictionary(
                kvp => kvp.Key,
                kvp => (
                    kvp.Value.Count,
                    kvp.Value.AverageMs,
                    kvp.Value.MaxMs,
                    kvp.Value.MinMs
                ));
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _metrics.Clear();
        }
    }

    public async Task ExportToJsonAsync(string filePath)
    {
        string json = ExportToJsonString();
        await File.WriteAllTextAsync(filePath, json);
    }

    private string ExportToJsonString()
    {
        Dictionary<string, (long Count, double AvgMs, double MaxMs, double MinMs)> metrics = GetMetrics();

        StringBuilder json = new();
        json.AppendLine("{");
        json.AppendLine($"  \"Timestamp\": \"{DateTime.UtcNow:O}\",");
        json.AppendLine($"  \"SessionDuration\": \"{DateTime.UtcNow.Subtract(_startTime)}\",");
        json.AppendLine("  \"Operations\": [");

        List<(string Name, long Count, double AvgMs, double MaxMs, double MinMs, double TotalMs)> operations =
            metrics.Select(kvp => (
                Name: EscapeJsonString(kvp.Key),
                Count: kvp.Value.Count,
                AvgMs: Math.Round(kvp.Value.AvgMs, 3),
                MaxMs: Math.Round(kvp.Value.MaxMs, 3),
                MinMs: Math.Round(kvp.Value.MinMs, 3),
                TotalMs: Math.Round(kvp.Value.Count * kvp.Value.AvgMs, 3)
            )).OrderByDescending(x => x.TotalMs).ToList();

        for (int i = 0; i < operations.Count; i++)
        {
            (string name, long count, double avgMs, double maxMs, double minMs, double totalMs) = operations[i];
            json.AppendLine("    {");
            json.AppendLine($"      \"Name\": \"{name}\",");
            json.AppendLine($"      \"ExecutionCount\": {count},");
            json.AppendLine($"      \"AverageMs\": {avgMs},");
            json.AppendLine($"      \"MaxMs\": {maxMs},");
            json.AppendLine($"      \"MinMs\": {minMs},");
            json.AppendLine($"      \"TotalMs\": {totalMs}");
            json.Append("    }");
            if (i < operations.Count - 1)
                json.AppendLine(",");
            else
                json.AppendLine();
        }

        json.AppendLine("  ]");
        json.AppendLine("}");

        return json.ToString();
    }

    private static string EscapeJsonString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        StringBuilder escaped = new(input.Length + 16);

        foreach (char c in input)
        {
            switch (c)
            {
                case '"':
                    escaped.Append("\\\"");
                    break;
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '\b':
                    escaped.Append("\\b");
                    break;
                case '\f':
                    escaped.Append("\\f");
                    break;
                case '\n':
                    escaped.Append("\\n");
                    break;
                case '\r':
                    escaped.Append("\\r");
                    break;
                case '\t':
                    escaped.Append("\\t");
                    break;
                default:
                    if (c < ' ')
                        escaped.Append($"\\u{(int)c:X4}");
                    else
                        escaped.Append(c);
                    break;
            }
        }

        return escaped.ToString();
    }

    private sealed class ProfileData
    {
        private long _count;
        private double _totalMs;
        private double _maxMs = double.MinValue;
        private double _minMs = double.MaxValue;

        public long Count => _count;
        public double AverageMs => _count > 0 ? _totalMs / _count : 0;
        public double MaxMs => _maxMs == double.MinValue ? 0 : _maxMs;
        public double MinMs => _minMs == double.MaxValue ? 0 : _minMs;

        public void RecordDuration(TimeSpan duration)
        {
            double ms = duration.TotalMilliseconds;
            _count++;
            _totalMs += ms;
            _maxMs = Math.Max(_maxMs, ms);
            _minMs = Math.Min(_minMs, ms);
        }
    }

    private sealed class OperationTimer(PerformanceProfiler profiler, string operationName) : IDisposable
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            _stopwatch.Stop();
            profiler.RecordOperation(operationName, _stopwatch.Elapsed);
            _disposed = true;
        }
    }
}