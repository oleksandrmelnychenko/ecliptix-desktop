using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.EcliptixProtocol;

namespace Ecliptix.Protected.Protocol.Native;

public static class ShamirSecretSharing
{
    private const int AUTH_KEY_SIZE = 32;

    public static Result<byte[][], EcliptixProtocolFailure> Split(
        byte[] secret,
        byte threshold,
        byte shareCount,
        byte[]? authKey = null)
    {
        if (secret.Length == 0)
        {
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Secret must not be empty"));
        }

        if (threshold < 2)
        {
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Threshold must be at least 2"));
        }

        if (shareCount < threshold)
        {
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Share count must be >= threshold"));
        }

        if (authKey is { Length: not AUTH_KEY_SIZE })
        {
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Auth key must be 32 bytes"));
        }

        IntPtr bufferPtr = NativeInterop.epp_buffer_alloc(0);
        if (bufferPtr == IntPtr.Zero)
        {
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.Generic("Failed to allocate native buffer for shares"));
        }

        NativeInterop.EppErrorCode result = NativeInterop.epp_shamir_split(
            secret,
            (nuint)secret.Length,
            threshold,
            shareCount,
            authKey,
            (nuint)(authKey?.Length ?? 0),
            bufferPtr,
            out nuint outShareLength,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            NativeInterop.epp_buffer_free(bufferPtr);
            return Result<byte[][], EcliptixProtocolFailure>.Err(ConvertError(result, errorMessage));
        }

        Result<byte[], EcliptixProtocolFailure> copyResult = CopyAndFree(bufferPtr);
        if (copyResult.IsErr)
        {
            return Result<byte[][], EcliptixProtocolFailure>.Err(copyResult.UnwrapErr());
        }

        byte[] sharesBuffer = copyResult.Unwrap();
        int shareLength = checked((int)outShareLength);
        if (shareLength <= 0 || sharesBuffer.Length % shareLength != 0)
        {
            CryptographicOperations.ZeroMemory(sharesBuffer);
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Invalid share buffer length"));
        }

        int actualShareCount = sharesBuffer.Length / shareLength;
        if (actualShareCount != shareCount)
        {
            CryptographicOperations.ZeroMemory(sharesBuffer);
            return Result<byte[][], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Share count mismatch"));
        }

        byte[][] shares = new byte[actualShareCount][];
        for (int i = 0; i < actualShareCount; i++)
        {
            byte[] share = new byte[shareLength];
            Buffer.BlockCopy(sharesBuffer, i * shareLength, share, 0, shareLength);
            shares[i] = share;
        }

        CryptographicOperations.ZeroMemory(sharesBuffer);
        return Result<byte[][], EcliptixProtocolFailure>.Ok(shares);
    }

    public static Result<byte[], EcliptixProtocolFailure> Reconstruct(
        IReadOnlyList<byte[]> shares,
        byte[]? authKey = null)
    {
        if (shares.Count == 0)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Shares are missing"));
        }

        if (authKey is { Length: not AUTH_KEY_SIZE })
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Auth key must be 32 bytes"));
        }

        int shareLength = shares[0]?.Length ?? 0;
        if (shareLength == 0)
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Share length is invalid"));
        }

        if (shares.Any(share => share.Length != shareLength))
        {
            return Result<byte[], EcliptixProtocolFailure>.Err(
                EcliptixProtocolFailure.InvalidInput("Share length mismatch"));
        }

        int totalLength = checked(shareLength * shares.Count);
        byte[] concatenated = new byte[totalLength];

        try
        {
            for (int i = 0; i < shares.Count; i++)
            {
                Buffer.BlockCopy(shares[i], 0, concatenated, i * shareLength, shareLength);
            }

            IntPtr bufferPtr = NativeInterop.epp_buffer_alloc(0);
            if (bufferPtr == IntPtr.Zero)
            {
                return Result<byte[], EcliptixProtocolFailure>.Err(
                    EcliptixProtocolFailure.Generic("Failed to allocate native buffer for secret"));
            }

            NativeInterop.EppErrorCode result = NativeInterop.epp_shamir_reconstruct(
                concatenated,
                (nuint)concatenated.Length,
                (nuint)shareLength,
                (nuint)shares.Count,
                authKey,
                (nuint)(authKey?.Length ?? 0),
                bufferPtr,
                out NativeInterop.EppError error);

            if (result != NativeInterop.EppErrorCode.Success)
            {
                string errorMessage = error.GetMessage();
                NativeInterop.epp_error_free(ref error);
                NativeInterop.epp_buffer_free(bufferPtr);
                return Result<byte[], EcliptixProtocolFailure>.Err(ConvertError(result, errorMessage));
            }

            return CopyAndFree(bufferPtr);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(concatenated);
        }
    }

    private static Result<byte[], EcliptixProtocolFailure> CopyAndFree(IntPtr bufferPtr)
    {
        try
        {
            NativeInterop.EppBuffer buffer = Marshal.PtrToStructure<NativeInterop.EppBuffer>(bufferPtr);
            int length = checked((int)buffer.Length);
            byte[] data = length == 0 ? [] : new byte[length];
            if (length > 0)
            {
                Marshal.Copy(buffer.Data, data, 0, length);
            }

            return Result<byte[], EcliptixProtocolFailure>.Ok(data);
        }
        finally
        {
            if (bufferPtr != IntPtr.Zero)
            {
                NativeInterop.epp_buffer_free(bufferPtr);
            }
        }
    }

    private static EcliptixProtocolFailure ConvertError(NativeInterop.EppErrorCode code, string message)
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
            _ => EcliptixProtocolFailure.Generic(message)
        };
    }
}
