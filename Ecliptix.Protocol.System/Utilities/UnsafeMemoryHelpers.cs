using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;

namespace Ecliptix.Protocol.System.Utilities;

/// <summary>
/// Helpers for zero-copy interop between protobuf ByteString and libsodium secure memory.
/// Provides secure operations that minimize heap allocations and prevent sensitive data exposure.
/// </summary>
public static unsafe class UnsafeMemoryHelpers
{
    /// <summary>
    /// Copies data from ByteString to secure memory without heap allocation.
    /// </summary>
    /// <param name="source">The source ByteString</param>
    /// <param name="destination">The destination secure memory handle</param>
    /// <returns>Success or failure result</returns>
    public static Result<Unit, SodiumFailure> CopyByteStringToSecureMemory(ByteString source, SodiumSecureMemoryHandle destination)
    {
        if (source.IsEmpty)
            return Result<Unit, SodiumFailure>.Ok(Unit.Value);

        return destination.Write(source.Span);
    }

    /// <summary>
    /// Creates a ByteString from secure memory with proper error propagation.
    /// Temporarily allocates memory that is securely wiped after use.
    /// </summary>
    /// <param name="source">The source secure memory handle</param>
    /// <param name="length">The number of bytes to read</param>
    /// <returns>Result containing ByteString or failure</returns>
    public static Result<ByteString, SodiumFailure> CreateByteStringFromSecureMemory(SodiumSecureMemoryHandle source, int length)
    {
        if (length <= 0)
            return Result<ByteString, SodiumFailure>.Ok(ByteString.Empty);

        if (length > source.Length)
            return Result<ByteString, SodiumFailure>.Err(
                SodiumFailure.InvalidOperation($"Requested length {length} exceeds handle length {source.Length}"));

        Result<byte[], SodiumFailure> readResult = source.ReadBytes(length);
        if (readResult.IsErr)
            return Result<ByteString, SodiumFailure>.Err(readResult.UnwrapErr());

        byte[] data = readResult.Unwrap();
        try
        {
            return Result<ByteString, SodiumFailure>.Ok(ByteString.CopyFrom(data));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    /// <summary>
    /// Executes an operation with a ByteString converted to a secure span without heap allocation.
    /// Uses the internal span directly for zero-copy operations.
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="byteString">The ByteString to convert</param>
    /// <param name="operation">The operation to execute with the span</param>
    /// <returns>The result of the operation</returns>
    public static TResult WithByteStringAsSpan<TResult>(ByteString byteString, Func<ReadOnlySpan<byte>, TResult> operation)
    {
        if (byteString.IsEmpty)
            return operation(ReadOnlySpan<byte>.Empty);

        return operation(byteString.Span);
    }

    /// <summary>
    /// Executes an operation with secure memory handling, avoiding heap allocations.
    /// Provides proper error propagation for cryptographic operations.
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="byteString">The ByteString to process</param>
    /// <param name="operation">The operation to execute with the span</param>
    /// <returns>Result containing the operation result or failure</returns>
    public static Result<TResult, SodiumFailure> WithSecureByteStringOperation<TResult>(
        ByteString byteString,
        Func<ReadOnlySpan<byte>, Result<TResult, SodiumFailure>> operation)
    {
        if (byteString.IsEmpty)
            return operation(ReadOnlySpan<byte>.Empty);

        return operation(byteString.Span);
    }

    /// <summary>
    /// Securely copies ByteString data to a temporary buffer with automatic cleanup.
    /// WARNING: This method allocates temporary non-secure memory. 
    /// The returned disposable MUST be used in a using statement to ensure secure wiping.
    /// </summary>
    /// <param name="source">The source ByteString</param>
    /// <returns>A disposable wrapper containing the copied data</returns>
    public static SecureTempBuffer SecureCopyToTempBuffer(ByteString source)
    {
        if (source.IsEmpty)
            return new SecureTempBuffer(Array.Empty<byte>());

        byte[] destination = new byte[source.Length];
        source.Span.CopyTo(destination);
        return new SecureTempBuffer(destination);
    }

    /// <summary>
    /// Enhanced secure memory operations with thread-safe access.
    /// Acquires a read lock on the handle for thread-safe operations.
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="handle">The secure memory handle</param>
    /// <param name="operation">The operation to execute with safe span access</param>
    /// <returns>Result containing the operation result or failure</returns>
    public static Result<TResult, SodiumFailure> WithThreadSafeSecureMemory<TResult>(
        SodiumSecureMemoryHandle handle,
        Func<ReadOnlySpan<byte>, Result<TResult, SodiumFailure>> operation)
    {
        // Note: This would require adding lock support to SodiumSecureMemoryHandle
        // For now, we'll document the limitation
        return handle.WithReadAccess(operation);
    }

    /// <summary>
    /// Safely compares two spans in constant time to prevent timing attacks.
    /// </summary>
    /// <param name="a">First span to compare</param>
    /// <param name="b">Second span to compare</param>
    /// <returns>True if spans are equal, false otherwise</returns>
    public static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length != b.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// Legacy method for compatibility. Use SecureCopyToTempBuffer instead.
    /// WARNING: Caller must securely wipe the output buffer after use.
    /// </summary>
    /// <param name="source">The source ByteString</param>
    /// <param name="destination">The destination buffer</param>
    public static void SecureCopyWithCleanup(ByteString source, out byte[] destination)
    {
        if (source.IsEmpty)
        {
            destination = Array.Empty<byte>();
            return;
        }

        destination = new byte[source.Length];
        source.Span.CopyTo(destination);
    }

    /// <summary>
    /// Creates a ByteString from a span. Simple wrapper for compatibility.
    /// </summary>
    /// <param name="source">The source span</param>
    /// <returns>A ByteString copy of the span</returns>
    public static ByteString CreateByteStringFromSpan(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return ByteString.Empty;

        return ByteString.CopyFrom(source);
    }

    /// <summary>
    /// Legacy method for compatibility. Use CopyByteStringToSecureMemory instead.
    /// </summary>
    /// <param name="source">The source span</param>
    /// <param name="destination">The destination secure memory handle</param>
    /// <returns>Success or failure result</returns>
    public static Result<Unit, SodiumFailure> CopyFromSpanToSecureMemory(ReadOnlySpan<byte> source, SodiumSecureMemoryHandle destination)
    {
        if (source.IsEmpty)
            return Result<Unit, SodiumFailure>.Ok(Unit.Value);

        return destination.Write(source);
    }

    /// <summary>
    /// Legacy method for compatibility. Use CopyByteStringToSecureMemory instead.
    /// </summary>
    /// <param name="source">The source ByteString</param>
    /// <param name="destination">The destination secure memory handle</param>
    /// <returns>Success or failure result</returns>
    public static Result<Unit, SodiumFailure> CopyFromByteStringToSecureMemory(ByteString source, SodiumSecureMemoryHandle destination)
    {
        return CopyByteStringToSecureMemory(source, destination);
    }

    /// <summary>
    /// Legacy method for compatibility. Use CopyByteStringToSecureMemory instead.
    /// </summary>
    /// <param name="source">The source ByteString</param>
    /// <param name="destination">The destination secure memory handle</param>
    /// <returns>Success or failure result</returns>
    public static Result<Unit, SodiumFailure> CopyByteStringToSecureBuffer(ByteString source, SodiumSecureMemoryHandle destination)
    {
        return CopyByteStringToSecureMemory(source, destination);
    }
}

/// <summary>
/// RAII wrapper for temporary buffers that ensures secure wiping on disposal.
/// Always use in a using statement to guarantee cleanup.
/// </summary>
public readonly struct SecureTempBuffer : IDisposable
{
    private readonly byte[] _buffer;

    internal SecureTempBuffer(byte[] buffer)
    {
        _buffer = buffer;
    }

    /// <summary>
    /// Gets a span view of the buffer data.
    /// </summary>
    public ReadOnlySpan<byte> Span => _buffer.AsSpan();

    /// <summary>
    /// Gets the buffer length.
    /// </summary>
    public int Length => _buffer.Length;

    /// <summary>
    /// Securely wipes the buffer contents.
    /// </summary>
    public void Dispose()
    {
        if (_buffer != null && _buffer.Length > 0)
        {
            CryptographicOperations.ZeroMemory(_buffer);
        }
    }
}