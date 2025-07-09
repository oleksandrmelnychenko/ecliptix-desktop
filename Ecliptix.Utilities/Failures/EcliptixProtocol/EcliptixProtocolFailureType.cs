namespace Ecliptix.Utilities.Failures.EcliptixProtocol;

public enum EcliptixProtocolFailureType
{
    Generic,
    DecodeFailed,
    DeriveKeyFailed,
    HandshakeFailed,
    PeerPubKeyFailed,
    InvalidInput,
    ObjectDisposed,
    AllocationFailed,
    PinningFailure,
    BufferTooSmall,
    DataTooLarge,
    KeyGenerationFailed,
    PrepareLocalFailed,
    MemoryBufferError,
}