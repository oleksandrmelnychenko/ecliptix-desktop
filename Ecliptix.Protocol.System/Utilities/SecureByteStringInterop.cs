using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;

namespace Ecliptix.Protocol.System.Utilities;

public static class SecureByteStringInterop
{

    public static Result<ByteString, SodiumFailure> CreateByteStringFromSecureMemory(SodiumSecureMemoryHandle source, int length)
    {
        ArgumentNullException.ThrowIfNull(source);

        switch (length)
        {
            case < 0:
                return Result<ByteString, SodiumFailure>.Err(
                    SodiumFailure.InvalidBufferSize($"Negative length requested: {length}"));
            case 0:
                return Result<ByteString, SodiumFailure>.Ok(ByteString.Empty);
        }

        if (length > source.Length)
            return Result<ByteString, SodiumFailure>.Err(
                SodiumFailure.InvalidBufferSize($"Requested length {length} exceeds handle length {source.Length}"));

        return source.WithReadAccess(span => Result<ByteString, SodiumFailure>.Ok(ByteString.CopyFrom(span.Slice(0, length))));
    }

    public static TResult WithByteStringAsSpan<TResult>(ByteString byteString, Func<ReadOnlySpan<byte>, TResult> operation)
    {
        ArgumentNullException.ThrowIfNull(byteString);
        ArgumentNullException.ThrowIfNull(operation);

        return operation(byteString.IsEmpty ? ReadOnlySpan<byte>.Empty : byteString.Span);
    }


    public static void SecureCopyWithCleanup(ByteString source, out byte[] destination)
    {
        if (source.IsEmpty)
        {
            destination = [];
            return;
        }

        destination = new byte[source.Length];
        source.Span.CopyTo(destination);
    }

    public static ByteString CreateByteStringFromSpan(ReadOnlySpan<byte> source)
    {
        return source.IsEmpty ? ByteString.Empty : ByteString.CopyFrom(source);
    }

    public static Result<Unit, SodiumFailure> CopyFromByteStringToSecureMemory(ByteString source, SodiumSecureMemoryHandle destination)
    {
        return source.IsEmpty ? Result<Unit, SodiumFailure>.Ok(Unit.Value) : destination.Write(source.Span);
    }
}