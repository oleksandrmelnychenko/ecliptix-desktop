using Google.Protobuf;

namespace Ecliptix.Utilities;

public static class ByteStringExtensions
{
    public static ByteString ToByteString(this Guid guid)
    {
        Span<byte> buffer = stackalloc byte[16];
        guid.TryWriteBytes(buffer);
        return ByteString.CopyFrom(buffer);
    }

    public static Guid ToGuid(this ByteString byteString) => byteString.IsEmpty ? Guid.Empty : new Guid(byteString.Span);
}
