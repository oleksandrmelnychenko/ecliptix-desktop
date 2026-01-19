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

        byte[] authKeyBytes = authKey ?? [];

        NativeInterop.EppErrorCode result = NativeInterop.epp_shamir_split(
            secret,
            (nuint)secret.Length,
            threshold,
            shareCount,
            authKeyBytes,
            (nuint)authKeyBytes.Length,
            out NativeInterop.EppBuffer buffer,
            out nuint outShareLength,
            out NativeInterop.EppError error);

        if (result != NativeInterop.EppErrorCode.Success)
        {
            string errorMessage = error.GetMessage();
            NativeInterop.epp_error_free(ref error);
            return Result<byte[][], EcliptixProtocolFailure>.Err(InteropHelpers.ConvertError(result, errorMessage));
        }

        Result<byte[], EcliptixProtocolFailure> copyResult = InteropHelpers.CopyBuffer(ref buffer, "Shares");
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

            byte[] authKeyBytes = authKey ?? [];

            NativeInterop.EppErrorCode result = NativeInterop.epp_shamir_reconstruct(
                concatenated,
                (nuint)concatenated.Length,
                (nuint)shareLength,
                (nuint)shares.Count,
                authKeyBytes,
                (nuint)authKeyBytes.Length,
                out NativeInterop.EppBuffer buffer,
                out NativeInterop.EppError error);

            if (result != NativeInterop.EppErrorCode.Success)
            {
                string errorMessage = error.GetMessage();
                NativeInterop.epp_error_free(ref error);
                return Result<byte[], EcliptixProtocolFailure>.Err(InteropHelpers.ConvertError(result, errorMessage));
            }

            return InteropHelpers.CopyBuffer(ref buffer, "Secret");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(concatenated);
        }
    }

}
