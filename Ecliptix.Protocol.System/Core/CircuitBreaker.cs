using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;
using Serilog;
using Serilog.Events;

namespace Ecliptix.Protocol.System.Core;

public enum CircuitBreakerState
{
    Closed,    // Normal operation
    Open,      // Failures detected, blocking requests
    HalfOpen   // Testing if service has recovered
}

public sealed class CircuitBreaker : IDisposable
{
    private readonly Lock _lock = new();
    private readonly int _failureThreshold;
    private readonly TimeSpan _timeout;
    private readonly double _successThresholdPercentage;

    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount;
    private int _successCount;
    private int _requestCount;
    private DateTime _lastFailureTime;
    private bool _disposed;

    public CircuitBreaker(
        int failureThreshold = 5,
        TimeSpan timeout = default,
        double successThresholdPercentage = 0.5)
    {
        _failureThreshold = failureThreshold;
        _timeout = timeout == TimeSpan.Zero ? TimeSpan.FromSeconds(30) : timeout;
        _successThresholdPercentage = successThresholdPercentage;
        _lastFailureTime = DateTime.MinValue;
    }

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public Result<T, EcliptixProtocolFailure> Execute<T>(Func<Result<T, EcliptixProtocolFailure>> operation)
    {
        if (_disposed)
            return Result<T, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Circuit breaker has been disposed"));

        // Check if we should allow the request
        Result<Unit, EcliptixProtocolFailure> canExecuteResult = CanExecute();
        if (canExecuteResult.IsErr)
            return Result<T, EcliptixProtocolFailure>.Err(canExecuteResult.UnwrapErr());

        try
        {
            Result<T, EcliptixProtocolFailure> result = operation();

            if (result.IsOk)
            {
                OnSuccess();
            }
            else
            {
                OnFailure();
            }

            return result;
        }
        catch (Exception ex)
        {
            OnFailure();
            return Result<T, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Operation failed in circuit breaker", ex));
        }
    }

    private Result<Unit, EcliptixProtocolFailure> CanExecute()
    {
        lock (_lock)
        {
            DateTime now = DateTime.UtcNow;

            switch (_state)
            {
                case CircuitBreakerState.Closed:
                    return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

                case CircuitBreakerState.Open:
                    if (now - _lastFailureTime >= _timeout)
                    {
                        // Move to half-open to test the service
                        _state = CircuitBreakerState.HalfOpen;
                        _requestCount = 0;
                        _successCount = 0;
                        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
                    }

                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic(
                            $"Circuit breaker is OPEN. Blocking requests until {_lastFailureTime.Add(_timeout):HH:mm:ss}"));

                case CircuitBreakerState.HalfOpen:
                    // Allow limited requests to test service recovery
                    if (_requestCount < _failureThreshold)
                    {
                        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
                    }

                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic("Circuit breaker is HALF-OPEN but testing limit reached"));

                default:
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic($"Unknown circuit breaker state: {_state}"));
            }
        }
    }

    private void OnSuccess()
    {
        lock (_lock)
        {
            _successCount++;
            _requestCount++;

            if (_state == CircuitBreakerState.HalfOpen)
            {
                // Check if we have enough successful requests to close the circuit
                double successRate = (double)_successCount / _requestCount;
                if (successRate >= _successThresholdPercentage && _requestCount >= _failureThreshold)
                {
                    // Service seems recovered, close the circuit
                    _state = CircuitBreakerState.Closed;
                    _failureCount = 0;
                    _successCount = 0;
                    _requestCount = 0;
                    if (Log.IsEnabled(LogEventLevel.Information))
                        Log.Information("Circuit breaker service recovered - Circuit CLOSED");
                }
            }
            else if (_state == CircuitBreakerState.Closed)
            {
                // Reset failure count on successful operation
                _failureCount = Math.Max(0, _failureCount - 1);
            }
        }
    }

    private void OnFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _requestCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_state == CircuitBreakerState.Closed && _failureCount >= _failureThreshold)
            {
                // Trip the circuit breaker
                _state = CircuitBreakerState.Open;
                if (Log.IsEnabled(LogEventLevel.Warning))
                    Log.Warning("Circuit breaker failure threshold ({FailureThreshold}) reached - Circuit OPEN", _failureThreshold);
            }
            else if (_state == CircuitBreakerState.HalfOpen)
            {
                // Service still failing, go back to open
                _state = CircuitBreakerState.Open;
                _successCount = 0;
                _requestCount = 0;
                if (Log.IsEnabled(LogEventLevel.Warning))
                    Log.Warning("Circuit breaker service still failing - Circuit OPEN again");
            }
        }
    }

    public (CircuitBreakerState State, int FailureCount, int SuccessCount, DateTime LastFailure) GetStatus()
    {
        lock (_lock)
        {
            return (_state, _failureCount, _successCount, _lastFailureTime);
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _successCount = 0;
            _requestCount = 0;
            _lastFailureTime = DateTime.MinValue;
            if (Log.IsEnabled(LogEventLevel.Information))
                Log.Information("Circuit breaker manually reset");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}