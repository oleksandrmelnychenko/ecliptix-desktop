namespace Ecliptix.Utilities.Failures.Network;

public enum NetworkFailureType
{
    DataCenterNotResponding,
    DataCenterShutdown,
    InvalidRequestType,
    EcliptixProtocolFailure,
    RsaEncryptionFailure,
    ProtocolStateMismatch,
}