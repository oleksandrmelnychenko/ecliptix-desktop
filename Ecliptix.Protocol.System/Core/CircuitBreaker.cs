using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Core;

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
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
        _timeout = timeout == TimeSpan.Zero ? ProtocolSystemConstants.Timeouts.DefaultCircuitBreakerTimeout : timeout;
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
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.CircuitBreaker.CircuitBreakerDisposed));

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
                EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.CircuitBreaker.OperationFailedInCircuitBreaker, ex));
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
                    if (now - _lastFailureTime < _timeout)
                        return Result<Unit, EcliptixProtocolFailure>.Err(
                            EcliptixProtocolFailure.Generic(
                                string.Format(EcliptixProtocolFailureMessages.CircuitBreaker.CircuitBreakerIsOpen, _lastFailureTime.Add(_timeout))));
                    _state = CircuitBreakerState.HalfOpen;
                    _requestCount = 0;
                    _successCount = 0;
                    return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);

                case CircuitBreakerState.HalfOpen:
                    if (_requestCount < _failureThreshold)
                    {
                        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
                    }

                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic(EcliptixProtocolFailureMessages.CircuitBreaker.CircuitBreakerHalfOpenTestingLimitReached));

                default:
                    return Result<Unit, EcliptixProtocolFailure>.Err(
                        EcliptixProtocolFailure.Generic(string.Format(EcliptixProtocolFailureMessages.CircuitBreaker.UnknownCircuitBreakerState, _state)));
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
                double successRate = (double)_successCount / _requestCount;
                if (successRate >= _successThresholdPercentage && _requestCount >= _failureThreshold)
                {
                    _state = CircuitBreakerState.Closed;
                    _failureCount = 0;
                    _successCount = 0;
                    _requestCount = 0;
                }
            }
            else if (_state == CircuitBreakerState.Closed)
            {
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
                _state = CircuitBreakerState.Open;
            }
            else if (_state == CircuitBreakerState.HalfOpen)
            {
                _state = CircuitBreakerState.Open;
                _successCount = 0;
                _requestCount = 0;
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
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}