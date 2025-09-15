using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;

namespace Ecliptix.Protocol.System.Utilities;

public static class SecureMemoryUtils
{
    private static readonly SecureMemoryPool DefaultPool = new(ProtocolSystemConstants.MemoryPool.DefaultBufferSize, ProtocolSystemConstants.MemoryPool.MaxPoolSize);

    public static Result<TResult, TError> WithSecureBuffer<TResult, TError>(
        int size,
        Func<Span<byte>, Result<TResult, TError>> operation)
        where TError : class
    {
        using SecureMemoryBuffer buffer = DefaultPool.Rent(size);

        byte[] fullBuffer = new byte[buffer.AllocatedSize];
        Result<Unit, SodiumFailure> readResult = buffer.Read(fullBuffer);
        if (readResult.IsErr)
            throw new InvalidOperationException(ProtocolSystemConstants.ErrorMessages.FailedToReadSecureMemory + readResult.UnwrapErr());

        Span<byte> span = fullBuffer.AsSpan(0, size);

        try
        {
            return operation(span);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(fullBuffer);
        }
    }

    public static Result<TResult, TError> WithSecureBuffers<TResult, TError>(
        int[] sizes,
        Func<SecureMemoryBuffer[], Result<TResult, TError>> operation)
        where TError : class
    {
        SecureMemoryBuffer[] buffers = new SecureMemoryBuffer[sizes.Length];

        try
        {
            for (int i = 0; i < sizes.Length; i++)
            {
                buffers[i] = DefaultPool.Rent(sizes[i]);
            }

            return operation(buffers);
        }
        finally
        {
            foreach (SecureMemoryBuffer buffer in buffers)
            {
                buffer?.Dispose();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(a, b);
    }
}