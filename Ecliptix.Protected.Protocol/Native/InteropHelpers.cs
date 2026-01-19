using System.Runtime.InteropServices;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protected.Protocol.Native;

internal static class InteropHelpers
{
    public static Result<byte[], EcliptixProtocolFailure> CopyBuffer(ref NativeInterop.EppBuffer buffer, string label)
    {
        try
        {
            if (buffer.Length == 0)
            {
                return Result<byte[], EcliptixProtocolFailure>.Ok([]);
            }

            if (buffer.Data == IntPtr.Zero)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput($"{label} buffer is null"));
            }

            if (buffer.Length > int.MaxValue)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.InvalidInput($"{label} length exceeds maximum array size"));
            }

            byte[] data = new byte[(int)buffer.Length];
            Marshal.Copy(buffer.Data, data, 0, data.Length);
            return Result<byte[], EcliptixProtocolFailure>.Ok(data);
        }
        finally
        {
            NativeInterop.epp_buffer_release(ref buffer);
        }
    }

    public static EcliptixProtocolFailure ConvertError(NativeInterop.EppErrorCode code, string message)
    {
        return code switch
        {
            NativeInterop.EppErrorCode.ErrorInvalidInput => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EppErrorCode.ErrorKeyGeneration => EcliptixProtocolFailure.KeyGeneration(message),
            NativeInterop.EppErrorCode.ErrorDeriveKey => EcliptixProtocolFailure.DeriveKey(message),
            NativeInterop.EppErrorCode.ErrorHandshake => EcliptixProtocolFailure.Handshake(message),
            NativeInterop.EppErrorCode.ErrorEncryption => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EppErrorCode.ErrorDecryption => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EppErrorCode.ErrorDecode => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EppErrorCode.ErrorEncode => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EppErrorCode.ErrorPqMissing => EcliptixProtocolFailure.Decode(message),
            NativeInterop.EppErrorCode.ErrorBufferTooSmall => EcliptixProtocolFailure.BufferTooSmall(message),
            NativeInterop.EppErrorCode.ErrorObjectDisposed => EcliptixProtocolFailure.ObjectDisposed(message),
            NativeInterop.EppErrorCode.ErrorPrepareLocal => EcliptixProtocolFailure.PrepareLocal(message),
            NativeInterop.EppErrorCode.ErrorOutOfMemory => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EppErrorCode.ErrorNullPointer => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EppErrorCode.ErrorInvalidState => EcliptixProtocolFailure.InvalidInput(message),
            NativeInterop.EppErrorCode.ErrorReplayAttack => EcliptixProtocolFailure.Generic(message),
            NativeInterop.EppErrorCode.ErrorSessionExpired => EcliptixProtocolFailure.SessionExpired(message),
            _ => EcliptixProtocolFailure.Generic(message)
        };
    }
}
