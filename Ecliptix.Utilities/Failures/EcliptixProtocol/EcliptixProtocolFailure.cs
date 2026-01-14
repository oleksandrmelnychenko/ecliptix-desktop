using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Utilities.Failures.EcliptixProtocol;

public record EcliptixProtocolFailure(
    EcliptixProtocolFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public static EcliptixProtocolFailure Generic(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.GENERIC, details, inner);

    public static EcliptixProtocolFailure Decode(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.DECODE_FAILED, details, inner);

    public static EcliptixProtocolFailure DeriveKey(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.DERIVE_KEY_FAILED, details, inner);

    public static EcliptixProtocolFailure Handshake(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.HANDSHAKE_FAILED, details, inner);

    public static EcliptixProtocolFailure PeerPubKey(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.PEER_PUB_KEY_FAILED, details, inner);

    public static EcliptixProtocolFailure InvalidInput(string details) => new(EcliptixProtocolFailureType.INVALID_INPUT, details);

    public static EcliptixProtocolFailure ObjectDisposed(string resourceName) => new(EcliptixProtocolFailureType.OBJECT_DISPOSED, $"Cannot access disposed resource '{resourceName}'.");

    public static EcliptixProtocolFailure AllocationFailed(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.ALLOCATION_FAILED, details, inner);

    public static EcliptixProtocolFailure PinningFailure(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.PINNING_FAILURE, details, inner);

    public static EcliptixProtocolFailure BufferTooSmall(string details) => new(EcliptixProtocolFailureType.BUFFER_TOO_SMALL, details);

    public static EcliptixProtocolFailure DataTooLarge(string details) => new(EcliptixProtocolFailureType.DATA_TOO_LARGE, details);

    public static EcliptixProtocolFailure KeyGeneration(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.KEY_GENERATION_FAILED, details, inner);

    public static EcliptixProtocolFailure PrepareLocal(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.PREPARE_LOCAL_FAILED, details, inner);

    public static EcliptixProtocolFailure MemoryBufferError(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.MEMORY_BUFFER_ERROR, details, inner);

    public static EcliptixProtocolFailure StateMismatch(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.STATE_MISMATCH, details, inner);

    public static EcliptixProtocolFailure SessionExpired(string details, Exception? inner = null) => new(EcliptixProtocolFailureType.SESSION_EXPIRED, details, inner);

    public override object ToStructuredLog() => new
    {
        ProtocolFailureType = FailureType.ToString(),
        Message,
        InnerException = InnerException?.Message,
        Timestamp
    };

    public NetworkFailure ToNetworkFailure()
    {
        NetworkFailureType networkFailureType = FailureType switch
        {
            EcliptixProtocolFailureType.STATE_MISMATCH => NetworkFailureType.PROTOCOL_STATE_MISMATCH,
            EcliptixProtocolFailureType.SESSION_EXPIRED => NetworkFailureType.SESSION_EXPIRED,
            _ => NetworkFailureType.ECLIPTIX_PROTOCOL_FAILURE
        };

        return new NetworkFailure(networkFailureType, Message, InnerException);
    }

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        FailureType switch
        {
            EcliptixProtocolFailureType.SESSION_EXPIRED => new GrpcErrorDescriptor(
                ErrorCode.UNAUTHENTICATED, StatusCode.Unauthenticated, ErrorI18NKeys.UNAUTHENTICATED),
            EcliptixProtocolFailureType.STATE_MISMATCH => new GrpcErrorDescriptor(
                ErrorCode.PRECONDITION_FAILED, StatusCode.FailedPrecondition, ErrorI18NKeys.PRECONDITION_FAILED),
            EcliptixProtocolFailureType.HANDSHAKE_FAILED => new GrpcErrorDescriptor(
                ErrorCode.PRECONDITION_FAILED, StatusCode.FailedPrecondition, ErrorI18NKeys.PRECONDITION_FAILED),
            EcliptixProtocolFailureType.PEER_PUB_KEY_FAILED => new GrpcErrorDescriptor(
                ErrorCode.PRECONDITION_FAILED, StatusCode.FailedPrecondition, ErrorI18NKeys.PRECONDITION_FAILED),
            EcliptixProtocolFailureType.PREPARE_LOCAL_FAILED => new GrpcErrorDescriptor(
                ErrorCode.PRECONDITION_FAILED, StatusCode.FailedPrecondition, ErrorI18NKeys.PRECONDITION_FAILED),
            EcliptixProtocolFailureType.INVALID_INPUT => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            EcliptixProtocolFailureType.BUFFER_TOO_SMALL => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            EcliptixProtocolFailureType.DATA_TOO_LARGE => new GrpcErrorDescriptor(
                ErrorCode.VALIDATION_FAILED, StatusCode.InvalidArgument, ErrorI18NKeys.VALIDATION),
            EcliptixProtocolFailureType.ALLOCATION_FAILED => new GrpcErrorDescriptor(
                ErrorCode.RESOURCE_EXHAUSTED, StatusCode.ResourceExhausted, ErrorI18NKeys.RESOURCE_EXHAUSTED),
            _ => new GrpcErrorDescriptor(
                ErrorCode.INTERNAL_ERROR, StatusCode.Internal, ErrorI18NKeys.INTERNAL)
        };
}
