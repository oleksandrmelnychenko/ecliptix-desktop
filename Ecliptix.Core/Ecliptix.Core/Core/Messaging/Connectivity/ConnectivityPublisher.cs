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
            if (intent.Reason == ConnectivityReason.None)
            {
                reason = intent.Failure is not null
                    ? ConnectivityReasonMapper.FromNetworkFailure(intent.Failure)
                    : ConnectivityReason.Unknown;
            }
            else
            {
                reason = intent.Reason;
            }

            if (reason == ConnectivityReason.None)
            {
                reason = ConnectivityReason.Unknown;
            }

            ConnectivitySnapshot next = new(
                intent.Status,
                reason,
                intent.Source,
                intent.Failure,
                intent.ConnectId,
                intent.RetryAttempt,
                intent.RetryBackoff,
                correlation,
                DateTime.UtcNow);

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
                return ConnectivityReason.Unknown;
            }

            return failure.FailureType switch
            {
                NetworkFailureType.DataCenterShutdown => ConnectivityReason.ServerShutdown,
                NetworkFailureType.OperationCancelled => ConnectivityReason.OperationCancelled,
                NetworkFailureType.CriticalAuthenticationFailure => ConnectivityReason.SecurityError,
                NetworkFailureType.InvalidRequestType => ConnectivityReason.SecurityError,
                NetworkFailureType.EcliptixProtocolFailure => ConnectivityReason.SecurityError,
                NetworkFailureType.RsaEncryptionFailure => ConnectivityReason.SecurityError,
                NetworkFailureType.ProtocolStateMismatch => ConnectivityReason.SecurityError,
                NetworkFailureType.ConnectionFailed => ConnectivityReason.HandshakeFailed,
                NetworkFailureType.DataCenterNotResponding => ConnectivityReason.RpcFailure,
                _ => ConnectivityReason.Unknown
            };
        }
    }
}
