using System;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Ecliptix.Utilities.Failures.Network;

namespace Ecliptix.Core.Core.Messaging.Connectivity;

internal sealed class ConnectivityPublisher : IDisposable
{
    private readonly BehaviorSubject<ConnectivitySnapshot> _snapshotStream;
    private readonly SemaphoreSlim _publishGate = new(1, 1);
    private ConnectivitySnapshot _currentSnapshot = ConnectivitySnapshot.Initial;

    public ConnectivityPublisher()
    {
        _snapshotStream = new BehaviorSubject<ConnectivitySnapshot>(_currentSnapshot);
    }

    public ConnectivitySnapshot CurrentSnapshot => _currentSnapshot;

    public IObservable<ConnectivitySnapshot> ConnectivityStream => _snapshotStream;

    public async Task PublishAsync(ConnectivityIntent intent, CancellationToken cancellationToken = default)
    {
        await _publishGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Guid correlation = intent.CorrelationId ?? Guid.NewGuid();

            ConnectivityReason reason;
            if (intent.Reason == ConnectivityReason.NONE)
            {
                reason = intent.Failure is not null
                    ? ConnectivityReasonMapper.FromNetworkFailure(intent.Failure)
                    : ConnectivityReason.UNKNOWN;
            }
            else
            {
                reason = intent.Reason;
            }

            if (reason == ConnectivityReason.NONE)
            {
                reason = ConnectivityReason.UNKNOWN;
            }

            ConnectivitySnapshot next = new(
                intent.Status,
                reason,
                intent.Source,
                intent.RetryAttempt,
                correlation);

            _currentSnapshot = next;
            _snapshotStream.OnNext(next);
        }
        finally
        {
            _publishGate.Release();
        }
    }

    public void Dispose()
    {
        _snapshotStream.Dispose();
        _publishGate.Dispose();
    }

    private static class ConnectivityReasonMapper
    {
        public static ConnectivityReason FromNetworkFailure(NetworkFailure? failure)
        {
            if (failure is null)
            {
                return ConnectivityReason.UNKNOWN;
            }

            return failure.FailureType switch
            {
                NetworkFailureType.DATA_CENTER_SHUTDOWN => ConnectivityReason.SERVER_SHUTDOWN,
                NetworkFailureType.OPERATION_CANCELLED => ConnectivityReason.OPERATION_CANCELLED,
                NetworkFailureType.CRITICAL_AUTHENTICATION_FAILURE => ConnectivityReason.SECURITY_ERROR,
                NetworkFailureType.INVALID_REQUEST_TYPE => ConnectivityReason.SECURITY_ERROR,
                NetworkFailureType.ECLIPTIX_PROTOCOL_FAILURE => ConnectivityReason.SECURITY_ERROR,
                NetworkFailureType.RSA_ENCRYPTION_FAILURE => ConnectivityReason.SECURITY_ERROR,
                NetworkFailureType.PROTOCOL_STATE_MISMATCH => ConnectivityReason.SECURITY_ERROR,
                NetworkFailureType.CONNECTION_FAILED => ConnectivityReason.HANDSHAKE_FAILED,
                NetworkFailureType.DATA_CENTER_NOT_RESPONDING => ConnectivityReason.RPC_FAILURE,
                _ => ConnectivityReason.UNKNOWN
            };
        }
    }
}
