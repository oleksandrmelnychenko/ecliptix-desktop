using Ecliptix.Utilities.Failures.Network;
using Grpc.Core;

namespace Ecliptix.Utilities.Failures.EcliptixProtocol;

public record EcliptixProtocolFailure(
    EcliptixProtocolFailureType FailureType,
    string Message,
    Exception? InnerException = null)
    : FailureBase(Message, InnerException)
{
    public static EcliptixProtocolFailure Generic(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.Generic, details, inner);
    }

    public static EcliptixProtocolFailure Decode(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.DecodeFailed, details, inner);
    }

    public static EcliptixProtocolFailure DeriveKey(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.DeriveKeyFailed, details, inner);
    }

    public static EcliptixProtocolFailure Handshake(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.HandshakeFailed, details, inner);
    }

    public static EcliptixProtocolFailure PeerPubKey(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.PeerPubKeyFailed, details, inner);
    }

    public static EcliptixProtocolFailure InvalidInput(string details)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.InvalidInput, details);
    }

    public static EcliptixProtocolFailure OBJECT_DISPOSED(string resourceName)
    {
        return new EcliptixProtocolFailure(
            EcliptixProtocolFailureType.OBJECT_DISPOSED, $"Cannot access disposed resource '{resourceName}'.");
    }

    public static EcliptixProtocolFailure ALLOCATION_FAILED(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.ALLOCATION_FAILED, details, inner);
    }

    public static EcliptixProtocolFailure PinningFailure(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.PinningFailure, details, inner);
    }

    public static EcliptixProtocolFailure BUFFER_TOO_SMALL(string details)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.BUFFER_TOO_SMALL, details);
    }

    public static EcliptixProtocolFailure DATA_TOO_LARGE(string details)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.DATA_TOO_LARGE, details);
    }

    public static EcliptixProtocolFailure KeyGeneration(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.KeyGenerationFailed, details, inner);
    }

    public static EcliptixProtocolFailure PrepareLocal(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.PrepareLocalFailed, details, inner);
    }

    public static EcliptixProtocolFailure MemoryBufferError(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.MemoryBufferError, details, inner);
    }

    public static EcliptixProtocolFailure StateMismatch(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.StateMismatch, details, inner);
    }

    public override object ToStructuredLog()
    {
        return new
        {
            ProtocolFailureType = FailureType.ToString(),
            Message,
            InnerException,
            Timestamp
        };
    }

    public NetworkFailure ToNetworkFailure()
    {
        NetworkFailureType networkFailureType = FailureType switch
        {
            EcliptixProtocolFailureType.StateMismatch => NetworkFailureType.ProtocolStateMismatch,
            _ => NetworkFailureType.EcliptixProtocolFailure
        };

        return new NetworkFailure(networkFailureType, Message, InnerException);
    }

    public override GrpcErrorDescriptor ToGrpcDescriptor() =>
        new(ERROR_CODE.InternalError, StatusCode.Internal, ErrorI18nKeys.INTERNAL);
}
