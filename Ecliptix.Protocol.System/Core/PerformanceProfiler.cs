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
        json.AppendLine(string.Format(ProtocolSystemConstants.JsonFormatting.TimestampField, DateTime.UtcNow));
        json.AppendLine(string.Format(ProtocolSystemConstants.JsonFormatting.SessionDurationField, DateTime.UtcNow.Subtract(_startTime)));
        json.AppendLine(ProtocolSystemConstants.JsonFormatting.OperationsArrayStart);

        List<(string Name, long Count, double AvgMs, double MaxMs, double MinMs, double TotalMs)> operations =
            metrics.Select(kvp => (
                Name: EscapeJsonString(kvp.Key),
                Count: kvp.Value.Count,
                AvgMs: Math.Round(kvp.Value.AvgMs, ProtocolSystemConstants.Numeric.PerformanceDecimalPlaces),
                MaxMs: Math.Round(kvp.Value.MaxMs, ProtocolSystemConstants.Numeric.PerformanceDecimalPlaces),
                MinMs: Math.Round(kvp.Value.MinMs, ProtocolSystemConstants.Numeric.PerformanceDecimalPlaces),
                TotalMs: Math.Round(kvp.Value.Count * kvp.Value.AvgMs, ProtocolSystemConstants.Numeric.PerformanceDecimalPlaces)
            )).OrderByDescending(x => x.TotalMs).ToList();

        for (int i = 0; i < operations.Count; i++)
        {
            (string name, long count, double avgMs, double maxMs, double minMs, double totalMs) = operations[i];
            json.AppendLine(ProtocolSystemConstants.JsonFormatting.OperationObjectStart);
            json.AppendLine(string.Format(ProtocolSystemConstants.JsonFormatting.NameField, name));
            json.AppendLine(string.Format(ProtocolSystemConstants.JsonFormatting.ExecutionCountField, count));
            json.AppendLine(string.Format(ProtocolSystemConstants.JsonFormatting.AverageField, avgMs));
            json.AppendLine(string.Format(ProtocolSystemConstants.JsonFormatting.MaxField, maxMs));
            json.AppendLine(string.Format(ProtocolSystemConstants.JsonFormatting.MinField, minMs));
            json.AppendLine(string.Format(ProtocolSystemConstants.JsonFormatting.TotalField, totalMs));
            json.Append(ProtocolSystemConstants.JsonFormatting.OperationObjectEnd);
            if (i < operations.Count - 1)
                json.AppendLine(",");
            else
                json.AppendLine();
        }

        json.AppendLine(ProtocolSystemConstants.JsonFormatting.ArrayEnd);
        json.AppendLine("}");

        return json.ToString();
    }

    private static string EscapeJsonString(string input)
    {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        StringBuilder escaped = new(input.Length + ProtocolSystemConstants.Numeric.JsonEscapeBufferExtra);

        foreach (char c in input)
        {
            switch (c)
            {
                case '"':
                    escaped.Append(ProtocolSystemConstants.JsonFormatting.EscapeQuote);
                    break;
                case '\\':
                    escaped.Append(ProtocolSystemConstants.JsonFormatting.EscapeBackslash);
                    break;
                case '\b':
                    escaped.Append(ProtocolSystemConstants.JsonFormatting.EscapeBackspace);
                    break;
                case '\f':
                    escaped.Append(ProtocolSystemConstants.JsonFormatting.EscapeFormFeed);
                    break;
                case '\n':
                    escaped.Append(ProtocolSystemConstants.JsonFormatting.EscapeNewLine);
                    break;
                case '\r':
                    escaped.Append(ProtocolSystemConstants.JsonFormatting.EscapeCarriageReturn);
                    break;
                case '\t':
                    escaped.Append(ProtocolSystemConstants.JsonFormatting.EscapeTab);
                    break;
                default:
                    if (c < ' ')
                        escaped.Append(string.Format(ProtocolSystemConstants.JsonFormatting.UnicodeEscapeFormat, (int)c));
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