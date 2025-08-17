using System.Runtime.InteropServices;
using Ecliptix.Protocol.System.Sodium;
using Ecliptix.Utilities;
using Ecliptix.Utilities.Failures.Sodium;
using Google.Protobuf;

namespace Ecliptix.Protocol.System.Utilities;

public static unsafe class UnsafeMemoryHelpers
{
    public static Result<Unit, SodiumFailure> CopyFromByteStringToSecureMemory(ByteString source, SodiumSecureMemoryHandle destination)
    {
        if (source.IsEmpty)
            return Result<Unit, SodiumFailure>.Ok(Unit.Value);
        
        ReadOnlySpan<byte> sourceSpan = source.Span;
        return CopyFromSpanToSecureMemory(sourceSpan, destination);
    }

    public static Result<Unit, SodiumFailure> CopyFromSpanToSecureMemory(ReadOnlySpan<byte> source, SodiumSecureMemoryHandle destination)
    {
        if (source.IsEmpty)
            return Result<Unit, SodiumFailure>.Ok(Unit.Value);

        fixed (byte* sourcePtr = source)
        {
            return destination.Write(source);
        }
    }


    public static ByteString CreateByteStringFromSecureMemory(SodiumSecureMemoryHandle source, int length)
    {
        Result<byte[], SodiumFailure> readResult = source.ReadBytes(length);
        if (readResult.IsErr)
            return ByteString.Empty;

        byte[] data = readResult.Unwrap();
        try
        {
            return ByteString.CopyFrom(data);
        }
        finally
        {
            SodiumInterop.SecureWipe(data).IgnoreResult();
        }
    }

    public static ByteString CreateByteStringFromSpan(ReadOnlySpan<byte> source)
    {
        if (source.IsEmpty)
            return ByteString.Empty;

        return ByteString.CopyFrom(source);
    }

    public static void SecureCopyWithCleanup(ByteString source, out byte[] destination)
    {
        if (source.IsEmpty)
        {
            destination = [];
            return;
        }

        destination = new byte[source.Length];
        fixed (byte* destPtr = destination)
        {
            source.Span.CopyTo(new Span<byte>(destPtr, source.Length));
        }
    }

}