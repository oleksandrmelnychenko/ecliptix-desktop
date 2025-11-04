namespace Ecliptix.Utilities.Failures.EcliptixProtocol;

public enum EcliptixProtocolFailureType
{
    Generic,
    DecodeFailed,
    DeriveKeyFailed,
    HandshakeFailed,
    PeerPubKeyFailed,
    InvalidInput,
    OBJECT_DISPOSED,
    ALLOCATION_FAILED,
    PinningFailure,
    BUFFER_TOO_SMALL,
    DATA_TOO_LARGE,
    KeyGenerationFailed,
    PrepareLocalFailed,
    MemoryBufferError,
    StateMismatch,
}
