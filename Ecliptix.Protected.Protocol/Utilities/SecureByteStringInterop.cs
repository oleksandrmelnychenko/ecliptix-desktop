using Google.Protobuf;

namespace Ecliptix.Protected.Protocol.Utilities;

public static class SecureByteStringInterop
{
    public static TResult WithByteStringAsSpan<TResult>(ByteString byteString,
        Func<ReadOnlySpan<byte>, TResult> operation) =>
        operation(byteString.IsEmpty ? [] : byteString.Span);
}
