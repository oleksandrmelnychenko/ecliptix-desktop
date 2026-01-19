using System.Text;
using Ecliptix.Protobuf.Protocol;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protected.Protocol.Native;

public sealed class NativeProtocolSession : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    internal NativeProtocolSession(IntPtr handle)
    {
        _handle = handle;
    }

    public static Result<NativeProtocolSession, EcliptixProtocolFailure> Import(byte[] stateBytes)
    {
        if (stateBytes == null)
        {
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("State bytes are null"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_deserialize(
            stateBytes,
            (nuint)stateBytes.Length,
            out IntPtr handle,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<NativeProtocolSession, EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        return Result<NativeProtocolSession, EcliptixProtocolFailure>.Ok(new NativeProtocolSession(handle));
    }

    public Result<byte[], EcliptixProtocolFailure> Encrypt(
        byte[] plaintext,
        EnvelopeType envelopeType,
        uint envelopeId,
        string? correlationId = null)
    {
        ThrowIfDisposed();

        if (plaintext == null)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Plaintext is null"));
        }

        byte[]? correlationBytes = null;
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            correlationBytes = Encoding.UTF8.GetBytes(correlationId);
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_encrypt(
            _handle,
            plaintext,
            (nuint)plaintext.Length,
            MapEnvelopeType(envelopeType),
            envelopeId,
            correlationBytes,
            (nuint)(correlationBytes?.Length ?? 0),
            out NativeInterop.EppBuffer buffer,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        return InteropHelpers.CopyBuffer(ref buffer, "Encrypted envelope");
    }

    public Result<ProtocolDecryptResult, EcliptixProtocolFailure> Decrypt(byte[] encryptedEnvelope)
    {
        ThrowIfDisposed();

        if (encryptedEnvelope == null)
        {
            return Result<ProtocolDecryptResult, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Encrypted envelope is null"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_decrypt(
            _handle,
            encryptedEnvelope,
            (nuint)encryptedEnvelope.Length,
            out NativeInterop.EppBuffer plaintextBuffer,
            out NativeInterop.EppBuffer metadataBuffer,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<ProtocolDecryptResult, EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        Result<byte[], EcliptixProtocolFailure> plaintextResult =
            InteropHelpers.CopyBuffer(ref plaintextBuffer, "Plaintext");
        if (plaintextResult.IsErr)
        {
            NativeInterop.epp_buffer_release(ref metadataBuffer);
            return Result<ProtocolDecryptResult, EcliptixProtocolFailure>.Err(plaintextResult.UnwrapErr());
        }

        Result<byte[], EcliptixProtocolFailure> metadataResult =
            InteropHelpers.CopyBuffer(ref metadataBuffer, "Metadata");
        if (metadataResult.IsErr)
        {
            return Result<ProtocolDecryptResult, EcliptixProtocolFailure>.Err(metadataResult.UnwrapErr());
        }

        return Result<ProtocolDecryptResult, EcliptixProtocolFailure>.Ok(
            new ProtocolDecryptResult(plaintextResult.Unwrap(), metadataResult.Unwrap()));
    }

    public Result<byte[], EcliptixProtocolFailure> ExportState()
    {
        ThrowIfDisposed();

        NativeInterop.EppErrorCode result = NativeInterop.epp_session_serialize(
            _handle,
            out NativeInterop.EppBuffer buffer,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[], EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        return InteropHelpers.CopyBuffer(ref buffer, "Session state");
    }

    public static Result<Unit, EcliptixProtocolFailure> ValidateEnvelope(byte[] encryptedEnvelope)
    {
        if (encryptedEnvelope == null)
        {
            return Result<Unit, EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Encrypted envelope is null"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_envelope_validate(
            encryptedEnvelope,
            (nuint)encryptedEnvelope.Length,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<Unit, EcliptixProtocolFailure>.Err(
                InteropHelpers.ConvertError(result, errorMessage));
        }

        return Result<Unit, EcliptixProtocolFailure>.Ok(Unit.Value);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_handle != IntPtr.Zero)
        {
            NativeInterop.epp_session_destroy(_handle);
            _handle = IntPtr.Zero;
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~NativeProtocolSession()
    {
        Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(NativeProtocolSession));
        }
    }

    private static NativeInterop.EppEnvelopeType MapEnvelopeType(EnvelopeType envelopeType) =>
        envelopeType switch
        {
            EnvelopeType.Request => NativeInterop.EppEnvelopeType.Request,
            EnvelopeType.Response => NativeInterop.EppEnvelopeType.Response,
            EnvelopeType.Notification => NativeInterop.EppEnvelopeType.Notification,
            EnvelopeType.Heartbeat => NativeInterop.EppEnvelopeType.Heartbeat,
            EnvelopeType.ErrorResponse => NativeInterop.EppEnvelopeType.ErrorResponse,
            _ => NativeInterop.EppEnvelopeType.Request
        };
}

public sealed class ProtocolDecryptResult
{
    public byte[] Plaintext { get; }
    public byte[] Metadata { get; }

    internal ProtocolDecryptResult(byte[] plaintext, byte[] metadata)
    {
        Plaintext = plaintext;
        Metadata = metadata;
    }
}
