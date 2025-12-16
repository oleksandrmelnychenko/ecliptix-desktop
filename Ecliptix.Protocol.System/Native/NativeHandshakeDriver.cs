using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protocol.System.Native;

/// <summary>
/// Small helper to drive the native handshake primitives (begin/complete) in one place.
/// This keeps NetworkProvider wiring lean and ensures consistent validation when we
/// migrate traffic onto the native PQ/hybrid path.
/// </summary>
public static class NativeHandshakeDriver
{
    public static Result<(NativeProtocolSession Session, byte[] Handshake), EcliptixProtocolFailure> Begin(
        EcliptixIdentityKeysWrapper identity,
        uint connectId,
        byte exchangeType,
        Action<uint>? onProtocolStateChanged = null)
    {
        Result<NativeProtocolSession, EcliptixProtocolFailure> sessionResult =
            NativeProtocolSystem.CreateSessionAdapter(identity, onProtocolStateChanged);
        if (sessionResult.IsErr)
        {
            return Result<(NativeProtocolSession, byte[]), EcliptixProtocolFailure>.Err(sessionResult.UnwrapErr());
        }

        NativeProtocolSession session = sessionResult.Unwrap();
        Result<byte[], EcliptixProtocolFailure> handshakeResult = session.BeginHandshake(connectId, exchangeType);
        if (handshakeResult.IsErr)
        {
            session.Dispose();
            return Result<(NativeProtocolSession, byte[]), EcliptixProtocolFailure>.Err(handshakeResult.UnwrapErr());
        }

        return Result<(NativeProtocolSession, byte[]), EcliptixProtocolFailure>.Ok(
            (session, handshakeResult.Unwrap()));
    }

    public static Result<Unit, EcliptixProtocolFailure> Complete(
        NativeProtocolSession session,
        byte[] peerHandshakeMessage,
        byte[] rootKey)
    {
        if (peerHandshakeMessage == null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Peer handshake is null"));
        }
        if (rootKey == null || rootKey.Length != 32)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Root key must be 32 bytes"));
        }

        return session.CompleteHandshake(peerHandshakeMessage, rootKey);
    }
}
