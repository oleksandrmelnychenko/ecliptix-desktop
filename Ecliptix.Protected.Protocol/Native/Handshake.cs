using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protected.Protocol.Native;

public sealed class NativeHandshakeInitiator : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    internal IntPtr Handle => _handle;

    public static Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure> Start(
        EcliptixIdentityKeysWrapper identityKeys,
        byte[] peerPreKeyBundle,
        uint maxMessagesPerChain)
    {
        if (identityKeys == null || identityKeys.IsDisposed)
        {
            return Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Identity keys are null or disposed"));
        }

        if (peerPreKeyBundle == null)
        {
            return Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Peer bundle is null"));
        }

        if (maxMessagesPerChain == 0)
        {
            return Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Max messages per chain must be greater than zero"));
        }

        NativeInterop.EppSessionConfig config = new()
        {
            MaxMessagesPerChain = maxMessagesPerChain
        };

        NativeInterop.EppErrorCode result = NativeInterop.epp_handshake_initiator_start(
            identityKeys.Handle,
            peerPreKeyBundle,
            (nuint)peerPreKeyBundle.Length,
            ref config,
            out IntPtr handle,
            out NativeInterop.EppBuffer buffer,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        Result<byte[], EcliptixProtocolFailure> messageResult =
            InteropHelpers.CopyBuffer(ref buffer, "Handshake init");
        if (messageResult.IsErr)
        {
            NativeInterop.epp_handshake_initiator_destroy(handle);
            return Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure>.Err(messageResult.UnwrapErr());
        }

        NativeHandshakeInitiator initiator = new(handle);
        return Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure>.Ok(
            new NativeHandshakeInitiatorStart(initiator, messageResult.Unwrap()));
    }

    public Result<NativeProtocolSession, EcliptixProtocolFailure> Finish(byte[] handshakeAck)
    {
        ThrowIfDisposed();

        if (handshakeAck == null)
        {
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Handshake ack is null"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_handshake_initiator_finish(
            _handle,
            handshakeAck,
            (nuint)handshakeAck.Length,
            out IntPtr sessionHandle,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        Dispose();
        return Result<NativeProtocolSession, EcliptixProtocolFailure>.Ok(new NativeProtocolSession(sessionHandle));
    }

    private NativeHandshakeInitiator(IntPtr handle)
    {
        _handle = handle;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NativeHandshakeInitiator));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            NativeInterop.epp_handshake_initiator_destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~NativeHandshakeInitiator()
    {
        Dispose();
    }
}

public sealed class NativeHandshakeInitiatorStart
{
    public NativeHandshakeInitiator Initiator { get; }
    public byte[] HandshakeInit { get; }

    internal NativeHandshakeInitiatorStart(NativeHandshakeInitiator initiator, byte[] handshakeInit)
    {
        Initiator = initiator;
        HandshakeInit = handshakeInit;
    }
}

public sealed class NativeHandshakeResponder : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    internal IntPtr Handle => _handle;

    public static Result<NativeHandshakeResponderStart, EcliptixProtocolFailure> Start(
        EcliptixIdentityKeysWrapper identityKeys,
        byte[] localPreKeyBundle,
        byte[] handshakeInit,
        uint maxMessagesPerChain)
    {
        if (identityKeys == null || identityKeys.IsDisposed)
        {
            return Result<NativeHandshakeResponderStart, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Identity keys are null or disposed"));
        }

        if (localPreKeyBundle == null)
        {
            return Result<NativeHandshakeResponderStart, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Local bundle is null"));
        }

        if (handshakeInit == null)
        {
            return Result<NativeHandshakeResponderStart, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Handshake init is null"));
        }

        if (maxMessagesPerChain == 0)
        {
            return Result<NativeHandshakeResponderStart, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Max messages per chain must be greater than zero"));
        }

        NativeInterop.EppSessionConfig config = new()
        {
            MaxMessagesPerChain = maxMessagesPerChain
        };

        NativeInterop.EppErrorCode result = NativeInterop.epp_handshake_responder_start(
            identityKeys.Handle,
            localPreKeyBundle,
            (nuint)localPreKeyBundle.Length,
            handshakeInit,
            (nuint)handshakeInit.Length,
            ref config,
            out IntPtr handle,
            out NativeInterop.EppBuffer buffer,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<NativeHandshakeResponderStart, EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        Result<byte[], EcliptixProtocolFailure> messageResult =
            InteropHelpers.CopyBuffer(ref buffer, "Handshake ack");
        if (messageResult.IsErr)
        {
            NativeInterop.epp_handshake_responder_destroy(handle);
            return Result<NativeHandshakeResponderStart, EcliptixProtocolFailure>.Err(messageResult.UnwrapErr());
        }

        NativeHandshakeResponder responder = new(handle);
        return Result<NativeHandshakeResponderStart, EcliptixProtocolFailure>.Ok(
            new NativeHandshakeResponderStart(responder, messageResult.Unwrap()));
    }

    public Result<NativeProtocolSession, EcliptixProtocolFailure> Finish()
    {
        ThrowIfDisposed();

        NativeInterop.EppErrorCode result = NativeInterop.epp_handshake_responder_finish(
            _handle,
            out IntPtr sessionHandle,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        Dispose();
        return Result<NativeProtocolSession, EcliptixProtocolFailure>.Ok(new NativeProtocolSession(sessionHandle));
    }

    private NativeHandshakeResponder(IntPtr handle)
    {
        _handle = handle;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NativeHandshakeResponder));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            NativeInterop.epp_handshake_responder_destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~NativeHandshakeResponder()
    {
        Dispose();
    }
}

public sealed class NativeHandshakeResponderStart
{
    public NativeHandshakeResponder Responder { get; }
    public byte[] HandshakeAck { get; }

    internal NativeHandshakeResponderStart(NativeHandshakeResponder responder, byte[] handshakeAck)
    {
        Responder = responder;
        HandshakeAck = handshakeAck;
    }
}
