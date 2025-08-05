using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Ecliptix.Protocol.System.Sodium;
using Sodium;

namespace Ecliptix.Protocol.System.Core;

public sealed class HkdfSha256 : IDisposable
{
    private const int HashOutputLength = 32;
    private bool _disposed;
    private readonly SodiumSecureMemoryHandle _ikmHandle;
    private readonly SodiumSecureMemoryHandle _saltHandle;

    public HkdfSha256(ReadOnlySpan<byte> ikm, ReadOnlySpan<byte> salt = default)
    {
        _ikmHandle = SodiumSecureMemoryHandle.Allocate(ikm.Length).Unwrap();
        _ikmHandle.Write(ikm).Unwrap();

        _saltHandle = SodiumSecureMemoryHandle.Allocate(HashOutputLength).Unwrap();
        if (salt.IsEmpty)
        {
            Span<byte> zeros = stackalloc byte[HashOutputLength];
            zeros.Clear();
            _saltHandle.Write(zeros).Unwrap();
        }
        else
        {
            if (salt.Length != HashOutputLength)
                throw new ArgumentException($@"Salt must be {HashOutputLength} bytes for HMAC-SHA256.", nameof(salt));
            _saltHandle.Write(salt).Unwrap();
        }

        _disposed = false;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Expand(ReadOnlySpan<byte> info, Span<byte> output)
    {
        if (!_disposed)
        {
            if (output.Length > 255 * HashOutputLength)
                throw new ArgumentException(@"Output length is too large for HKDF-SHA256.", nameof(output));
            if (output.IsEmpty) return;

            Span<byte> prk = stackalloc byte[HashOutputLength];
            byte[]? ikmBytes = null;
            byte[]? saltBytes = null;
            try
            {
                ikmBytes = _ikmHandle.ReadBytes(_ikmHandle.Length).Unwrap();
                saltBytes = _saltHandle.ReadBytes(_saltHandle.Length).Unwrap();
                byte[] prkResult = SecretKeyAuth.SignHmacSha256(ikmBytes, saltBytes);
                prkResult.CopyTo(prk);
                SodiumInterop.SecureWipe(prkResult);
            }
            catch (Exception ex)
            {
                throw new CryptographicException("HKDF-Extract (PRK generation) failed.", ex);
            }
            finally
            {
                SodiumInterop.SecureWipe(ikmBytes);
                SodiumInterop.SecureWipe(saltBytes);
            }

            int bytesWritten = 0;
            byte counter = 1;
            Span<byte> previousHash = stackalloc byte[HashOutputLength];

            int maxHmacInputSize = HashOutputLength + info.Length + 1;
            byte[]? hmacInputBuffer = null;
            byte[]? prkBuffer = null;

            try
            {
                hmacInputBuffer = ArrayPool<byte>.Shared.Rent(maxHmacInputSize);
                prkBuffer = ArrayPool<byte>.Shared.Rent(HashOutputLength);
                prk.CopyTo(prkBuffer);

                while (bytesWritten < output.Length)
                {
                    Span<byte> currentInputSpan = hmacInputBuffer.AsSpan(0, maxHmacInputSize);
                    int currentInputLength;

                    if (bytesWritten == 0)
                    {
                        info.CopyTo(currentInputSpan);
                        currentInputSpan[info.Length] = counter;
                        currentInputLength = info.Length + 1;
                    }
                    else
                    {
                        previousHash.CopyTo(currentInputSpan);
                        info.CopyTo(currentInputSpan[HashOutputLength..]);
                        currentInputSpan[HashOutputLength + info.Length] = counter;
                        currentInputLength = HashOutputLength + info.Length + 1;
                    }

                    byte[] tempHashResult =
                        SecretKeyAuth.SignHmacSha256(hmacInputBuffer[..currentInputLength], prkBuffer);

                    int bytesToCopy = Math.Min(HashOutputLength, output.Length - bytesWritten);
                    tempHashResult.AsSpan(0, bytesToCopy).CopyTo(output[bytesWritten..]);
                    bytesWritten += bytesToCopy;

                    if (bytesWritten < output.Length)
                    {
                        tempHashResult.CopyTo(previousHash);
                    }

                    SodiumInterop.SecureWipe(tempHashResult);
                    counter++;
                }
            }
            finally
            {
                prk.Clear();
                previousHash.Clear();
                if (hmacInputBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(hmacInputBuffer, clearArray: true);
                }

                if (prkBuffer != null)
                {
                    ArrayPool<byte>.Shared.Return(prkBuffer, clearArray: true);
                }
            }
        }
        else
        {
            throw new ObjectDisposedException(nameof(HkdfSha256));
        }
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;
        if (disposing)
        {
            _ikmHandle.Dispose();
            _saltHandle.Dispose();
        }

        _disposed = true;
    }
}