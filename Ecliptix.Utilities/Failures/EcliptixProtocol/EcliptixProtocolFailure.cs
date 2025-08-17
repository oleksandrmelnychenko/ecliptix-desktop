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

    public static EcliptixProtocolFailure ObjectDisposed(string resourceName)
    {
        return new EcliptixProtocolFailure(
            EcliptixProtocolFailureType.ObjectDisposed, $"Cannot access disposed resource '{resourceName}'.");
    }

    public static EcliptixProtocolFailure AllocationFailed(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.AllocationFailed, details, inner);
    }

    public static EcliptixProtocolFailure PinningFailure(string details, Exception? inner = null)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.PinningFailure, details, inner);
    }

    public static EcliptixProtocolFailure BufferTooSmall(string details)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.BufferTooSmall, details);
    }

    public static EcliptixProtocolFailure DataTooLarge(string details)
    {
        return new EcliptixProtocolFailure(EcliptixProtocolFailureType.DataTooLarge, details);
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
            _ => NetworkFailureType.EcliptixProtocolFailure
        };

        return new NetworkFailure(networkFailureType, Message, InnerException);
    }
}