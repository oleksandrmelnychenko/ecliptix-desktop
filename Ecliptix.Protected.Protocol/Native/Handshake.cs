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
        if (identityKeys.IsDisposed)
        {
            return Result<NativeHandshakeInitiatorStart, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Identity keys are null or disposed"));
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

