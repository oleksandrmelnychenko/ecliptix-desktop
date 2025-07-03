namespace Ecliptix.Utilities.Failures.EcliptixProtocol;

public enum EcliptixProtocolFailureType
{
    Generic,
    DecodeFailed,
    ActorRefNotFound,
    ActorNotCreated,
    DeriveKeyFailed,
    HandshakeFailed,
    PeerPubKeyFailed,
    InvalidInput,
    ObjectDisposed,
    EphemeralMissing,
    StateMissing,
    AllocationFailed,
    PinningFailure,
    BufferTooSmall,
    DataTooLarge,
    KeyGenerationFailed,
    PrepareLocalFailed,
    MemoryBufferError
}